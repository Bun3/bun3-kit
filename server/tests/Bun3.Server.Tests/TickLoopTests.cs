using System.Collections.Concurrent;
using Bun3.Server.Ticking;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class TickLoopTests
{
    [Test]
    public async Task Every_job_runs_repeatedly_with_sane_delta()
    {
        var deltas = new ConcurrentQueue<TimeSpan>();
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
        loop.Every(TimeSpan.FromMilliseconds(50), delta =>
        {
            deltas.Enqueue(delta);
            return default;
        });

        loop.Start();
        await Task.Delay(700);
        await loop.StopAsync();

        Assert.That(deltas.Count, Is.GreaterThanOrEqualTo(3));
        foreach (var delta in deltas)
        {
            Assert.That(delta, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(delta, Is.LessThan(TimeSpan.FromSeconds(2)));   // CI 스톨 대비 느슨한 상한
        }
    }

    [Test]
    public async Task Job_exception_does_not_kill_loop_or_other_jobs()
    {
        var healthyRuns = 0;
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
        loop.Every(TimeSpan.FromMilliseconds(30), _ => throw new InvalidOperationException("boom"), "bomb");
        loop.Every(TimeSpan.FromMilliseconds(30), _ =>
        {
            Interlocked.Increment(ref healthyRuns);
            return default;
        }, "healthy");

        loop.Start();
        await Task.Delay(400);
        await loop.StopAsync();

        Assert.That(healthyRuns, Is.GreaterThanOrEqualTo(3));   // 폭탄 잡이 루프를 못 죽였다
    }

    [Test]
    public void Registration_after_start_throws()
    {
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
        loop.Start();
        try
        {
            Assert.Throws<InvalidOperationException>(() => loop.Every(TimeSpan.FromSeconds(1), _ => default));
            Assert.Throws<InvalidOperationException>(() =>
                loop.DailyAt(TimeSpan.FromHours(5), TimeSpan.Zero, () => default));
        }
        finally
        {
            loop.StopAsync().GetAwaiter().GetResult();
        }
    }

    [Test]
    public async Task StopAsync_waits_for_inflight_job()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(10) });
        loop.Every(TimeSpan.FromMilliseconds(10), async _ =>
        {
            entered.TrySetResult(true);
            await Task.Delay(150);
            Interlocked.Increment(ref completed);
        });

        loop.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await loop.StopAsync();

        Assert.That(completed, Is.GreaterThanOrEqualTo(1));   // 진행 중이던 잡이 끝난 뒤에 정지했다
    }

    // NextDailyOccurrence — 순수 함수 결정적 검증 (가짜 시계 불필요)
    [TestCase("2026-01-15T02:00:00+00:00", 5, 0, "2026-01-15T05:00:00+00:00")]   // 오늘 아직 안 지남
    [TestCase("2026-01-15T06:00:00+00:00", 5, 0, "2026-01-16T05:00:00+00:00")]   // 오늘 지남 → 내일
    [TestCase("2026-01-14T21:00:00+00:00", 5, 9, "2026-01-15T20:00:00+00:00")]   // KST 06:00 → 다음 KST 05:00
    [TestCase("2026-01-15T20:00:00+00:00", 5, 9, "2026-01-16T20:00:00+00:00")]   // 정확히 발생 시각 → 다음날 (전진 보장)
    public void NextDailyOccurrence_computes_next_fire_time(
        string nowIso, int hourOfDay, int offsetHours, string expectedIso)
    {
        var next = TickLoop.NextDailyOccurrence(
            DateTimeOffset.Parse(nowIso),
            TimeSpan.FromHours(hourOfDay),
            TimeSpan.FromHours(offsetHours));

        Assert.That(next, Is.EqualTo(DateTimeOffset.Parse(expectedIso)));
    }

    [Test]
    public async Task DailyAt_fires_when_time_of_day_arrives()
    {
        var now = DateTimeOffset.UtcNow;
        var timeOfDay = now.TimeOfDay + TimeSpan.FromMilliseconds(300);
        if (timeOfDay >= TimeSpan.FromHours(24))
        {
            Assert.Inconclusive("자정 직전 — 재실행하면 통과한다.");
        }

        var fired = 0;
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
        loop.DailyAt(timeOfDay, TimeSpan.Zero, () =>
        {
            Interlocked.Increment(ref fired);
            return default;
        });

        loop.Start();
        await Task.Delay(1200);
        await loop.StopAsync();

        Assert.That(fired, Is.EqualTo(1));   // 발화 1회 — 다음 발생은 내일이므로 재발화 없음
    }
}
