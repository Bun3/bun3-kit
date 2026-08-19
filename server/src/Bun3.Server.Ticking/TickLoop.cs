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
    /// Global tick loop — runs registered jobs sequentially on one flow. Job exceptions are
    /// isolated (logged and the loop continues) and waits compensate for drift. Jobs must be
    /// short — a slow job delays the others; offload heavy work to a separate Task from within
    /// the job. Registration (Every/DailyAt) is only allowed before Start.
    /// </summary>
    public sealed class TickLoop
    {
        private static readonly TimeSpan MinDelay = TimeSpan.FromMilliseconds(10);

        private sealed class Job
        {
            public readonly string Name;
            public readonly Func<DateTimeOffset, ValueTask> Run;
            public readonly Func<DateTimeOffset, DateTimeOffset, DateTimeOffset> Advance;   // (now, previous NextAt) → next NextAt
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

        /// <summary>Constructs the tick loop. Nothing runs until Start.</summary>
        public TickLoop(TickingOptions? options = null, ILogger? logger = null)
        {
            var effective = options ?? new TickingOptions();
            if (effective.TickInterval <= TimeSpan.Zero)
            {
                throw new ArgumentException("TickInterval must be positive.", nameof(options));
            }

            _tickInterval = effective.TickInterval;
            _timeProvider = effective.TimeProvider ?? TimeProvider.System;
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
        }

        /// <summary>Registers a fixed-interval job. The job's argument is the actual elapsed time since its last run.</summary>
        public void Every(TimeSpan interval, Func<TimeSpan, ValueTask> job, string? name = null)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentException("Interval must be positive.", nameof(interval));
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
                    return next > now ? next : now + interval;   // Missed occurrences do not fire in a burst.
                },
                firstAt: lastRunAt + interval));
        }

        /// <summary>Registers a job that fires daily at the given time of day (UTC).
        /// Only UTC is supported — for local times the game converts before passing.
        /// Occurrences missed while the server was down are not caught up — determine
        /// "did I get today's reset" from game data.</summary>
        public void DailyAt(TimeSpan timeOfDay, Func<ValueTask> job, string? name = null)
        {
            if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromHours(24))
            {
                throw new ArgumentException("timeOfDay must be within [0, 24h).", nameof(timeOfDay));
            }
            if (job == null) throw new ArgumentNullException(nameof(job));
            EnsureNotStarted();

            _jobs.Add(new Job(
                name ?? string.Format(CultureInfo.InvariantCulture, "daily-{0:hh\\:mm}", timeOfDay),
                run: _ => job(),
                advance: (now, _) => NextDailyOccurrence(now, timeOfDay),
                firstAt: NextDailyOccurrence(_timeProvider.GetUtcNow(), timeOfDay)));
        }

        /// <summary>Computes the next occurrence (UTC) — the first timeOfDay strictly after nowUtc.
        /// If nowUtc equals the occurrence exactly, advances to the next day (prevents double firing).</summary>
        public static DateTimeOffset NextDailyOccurrence(DateTimeOffset nowUtc, TimeSpan timeOfDay)
        {
            var utc = nowUtc.ToUniversalTime();
            var todayAt = new DateTimeOffset(utc.Date, TimeSpan.Zero) + timeOfDay;
            return todayAt > utc ? todayAt : todayAt.AddDays(1);
        }

        /// <summary>Starts the loop. May only be called once.</summary>
        public void Start()
        {
            if (_runTask != null)
            {
                throw new InvalidOperationException("TickLoop has already been started.");
            }

            _runTask = Task.Run(() => RunAsync(_cts.Token));
        }

        /// <summary>Stops the loop — waits for the in-flight tick (job) to finish.
        /// ct only means "give up waiting": cancellation never force-aborts the loop
        /// (serialization/no-interruption philosophy); a loop that has seen the cancel signal
        /// exits on its own as soon as the current job finishes.</summary>
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
            await completed.ConfigureAwait(false);   // Loop completed, or TaskCanceledException propagates.
        }

        private void EnsureNotStarted()
        {
            if (_runTask != null)
            {
                throw new InvalidOperationException("Jobs can only be registered before Start.");
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
                            _logger.LogError(ex, "Tick job {Job} threw — loop continues.", job.Name);
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
                // Normal stop via StopAsync.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tick loop terminated abnormally.");
            }
        }
    }
}
