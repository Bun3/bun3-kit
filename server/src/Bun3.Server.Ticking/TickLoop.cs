using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Ticking
{
    /// <summary>
    /// 전역 틱 루프 — 등록된 잡을 한 흐름에서 순차 실행한다. 잡 예외는 격리(로그 후
    /// 계속)되고 대기는 드리프트를 보정한다. 잡은 짧아야 한다 — 오래 걸리면 다른
    /// 잡이 밀린다. 무거운 작업은 잡 안에서 별도 Task로 던질 것.
    /// 등록(Every/DailyAt)은 Start 전에만 허용된다.
    /// </summary>
    public sealed class TickLoop
    {
        private static readonly TimeSpan MinDelay = TimeSpan.FromMilliseconds(10);

        private sealed class Job
        {
            public readonly string Name;
            public readonly Func<DateTimeOffset, ValueTask> Run;
            public readonly Func<DateTimeOffset, DateTimeOffset, DateTimeOffset> Advance;   // (now, 이전 NextAt) → 다음 NextAt
            public DateTimeOffset NextAt;

            public Job(string name, Func<DateTimeOffset, ValueTask> run,
                Func<DateTimeOffset, DateTimeOffset, DateTimeOffset> advance, DateTimeOffset firstAt)
            {
                Name = name;
                Run = run;
                Advance = advance;
                NextAt = firstAt;
            }
        }

        private readonly TimeSpan _tickInterval;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly List<Job> _jobs = new List<Job>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _runTask;

        /// <summary>틱 루프를 구성한다. Start 전까지는 아무것도 돌지 않는다.</summary>
        public TickLoop(TickingOptions? options = null, ILogger? logger = null)
        {
            var effective = options ?? new TickingOptions();
            if (effective.TickInterval <= TimeSpan.Zero)
            {
                throw new ArgumentException("TickInterval은 양수여야 한다.", nameof(options));
            }

            _tickInterval = effective.TickInterval;
            _timeProvider = effective.TimeProvider ?? TimeProvider.System;
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
        }

        /// <summary>고정 간격 잡을 등록한다. job의 인자는 이 잡의 지난 실행 이후 실제 경과 시간.</summary>
        public void Every(TimeSpan interval, Func<TimeSpan, ValueTask> job, string? name = null)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentException("간격은 양수여야 한다.", nameof(interval));
            }
            if (job == null) throw new ArgumentNullException(nameof(job));
            EnsureNotStarted();

            var lastRunAt = _timeProvider.GetUtcNow();
            _jobs.Add(new Job(
                name ?? string.Format(CultureInfo.InvariantCulture, "every-{0:F0}ms", interval.TotalMilliseconds),
                run: now =>
                {
                    var delta = now - lastRunAt;
                    lastRunAt = now;
                    return job(delta);
                },
                advance: (now, previousNextAt) =>
                {
                    var next = previousNextAt + interval;
                    return next > now ? next : now + interval;   // 밀린 만큼 몰아서 발화하지 않는다
                },
                firstAt: lastRunAt + interval));
        }

        /// <summary>매일 지정 시각(UTC 기준 하루 중 시각)에 발화하는 잡을 등록한다.
        /// 시간대는 UTC 하나만 지원한다 — 지역별 시각이 필요하면 게임이 환산해서 넘긴다.
        /// 서버가 꺼져 있던 사이의 발생은 캐치업하지 않는다 — "오늘 리셋을 받았나"는
        /// 게임 데이터로 판정할 것(스펙 §8 권장 패턴).</summary>
        public void DailyAt(TimeSpan timeOfDay, Func<ValueTask> job, string? name = null)
        {
            if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromHours(24))
            {
                throw new ArgumentException("timeOfDay는 [0, 24시간) 범위여야 한다.", nameof(timeOfDay));
            }
            if (job == null) throw new ArgumentNullException(nameof(job));
            EnsureNotStarted();

            _jobs.Add(new Job(
                name ?? string.Format(CultureInfo.InvariantCulture, "daily-{0:hh\\:mm}", timeOfDay),
                run: _ => job(),
                advance: (now, _) => NextDailyOccurrence(now, timeOfDay),
                firstAt: NextDailyOccurrence(_timeProvider.GetUtcNow(), timeOfDay)));
        }

        /// <summary>다음 발생 시각(UTC)을 계산한다 — nowUtc "이후"의 첫 timeOfDay.
        /// 정확히 발생 시각과 같으면 다음날로 전진한다(중복 발화 방지).</summary>
        public static DateTimeOffset NextDailyOccurrence(DateTimeOffset nowUtc, TimeSpan timeOfDay)
        {
            var utc = nowUtc.ToUniversalTime();
            var todayAt = new DateTimeOffset(utc.Date, TimeSpan.Zero) + timeOfDay;
            return todayAt > utc ? todayAt : todayAt.AddDays(1);
        }

        /// <summary>루프를 시작한다. 1회만 호출 가능.</summary>
        public void Start()
        {
            if (_runTask != null)
            {
                throw new InvalidOperationException("TickLoop은 이미 시작되었다.");
            }

            _runTask = Task.Run(() => RunAsync(_cts.Token));
        }

        /// <summary>루프를 정지한다 — 진행 중인 틱(잡)이 끝날 때까지 기다린다.
        /// ct는 "기다림의 포기"만 의미한다: 취소되어도 루프 강제 중단은 없으며(직렬화/무중단 철학),
        /// 이미 취소 신호를 받은 루프는 현재 잡이 끝나는 대로 스스로 종료한다.</summary>
        public async Task StopAsync(CancellationToken ct = default)
        {
            _cts.Cancel();
            if (_runTask == null)
            {
                return;
            }

            if (!ct.CanBeCanceled)
            {
                await _runTask.ConfigureAwait(false);
                return;
            }

            var abandon = Task.Delay(System.Threading.Timeout.Infinite, ct);
            var completed = await Task.WhenAny(_runTask, abandon).ConfigureAwait(false);
            await completed.ConfigureAwait(false);   // 루프 완료 또는 TaskCanceledException 전파
        }

        private void EnsureNotStarted()
        {
            if (_runTask != null)
            {
                throw new InvalidOperationException("잡 등록은 Start 전에만 가능하다.");
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var tickStart = _timeProvider.GetUtcNow();
                    foreach (var job in _jobs)
                    {
                        if (job.NextAt > tickStart)
                        {
                            continue;
                        }

                        try
                        {
                            await job.Run(tickStart).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "틱 잡 {Job} 예외 — 루프는 계속된다.", job.Name);
                        }

                        job.NextAt = job.Advance(tickStart, job.NextAt);
                    }

                    var elapsed = _timeProvider.GetUtcNow() - tickStart;
                    var wait = _tickInterval - elapsed;
                    if (wait < MinDelay)
                    {
                        wait = MinDelay;
                    }

                    await _timeProvider.Delay(wait, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // StopAsync에 의한 정상 정지
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "틱 루프 비정상 종료.");
            }
        }
    }
}
