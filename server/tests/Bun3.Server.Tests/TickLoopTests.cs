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
            Assert.That(delta, Is.LessThan(TimeSpan.FromSeconds(2)));   // loose upper bound to tolerate CI stalls
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

        Assert.That(healthyRuns, Is.GreaterThanOrEqualTo(3));   // the bomb job could not kill the loop
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
                loop.DailyAt(TimeSpan.FromHours(5), () => default));
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

        Assert.That(completed, Is.GreaterThanOrEqualTo(1));   // stop happened only after the in-flight job finished
    }

    // NextDailyOccurrence — deterministic pure-function checks (no fake clock needed, UTC only)
    [TestCase("2026-01-15T02:00:00+00:00", 5, "2026-01-15T05:00:00+00:00")]   // not yet passed today
    [TestCase("2026-01-15T06:00:00+00:00", 5, "2026-01-16T05:00:00+00:00")]   // passed today -> tomorrow
    [TestCase("2026-01-15T05:00:00+00:00", 5, "2026-01-16T05:00:00+00:00")]   // exactly at fire time -> next day (guaranteed progress)
    [TestCase("2026-01-15T05:00:00+09:00", 20, "2026-01-15T20:00:00+00:00")]  // non-UTC offset now is normalized to UTC
    public void NextDailyOccurrence_computes_next_fire_time(
        string nowIso, int hourOfDay, string expectedIso)
    {
        var next = TickLoop.NextDailyOccurrence(
            DateTimeOffset.Parse(nowIso),
            TimeSpan.FromHours(hourOfDay));

        Assert.That(next, Is.EqualTo(DateTimeOffset.Parse(expectedIso)));
    }

    [Test]
    public async Task DailyAt_fires_when_time_of_day_arrives()
    {
        var now = DateTimeOffset.UtcNow;
        var timeOfDay = now.TimeOfDay + TimeSpan.FromMilliseconds(300);
        if (timeOfDay >= TimeSpan.FromHours(24))
        {
            Assert.Inconclusive("Just before midnight — rerun to pass.");
        }

        var fired = 0;
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
        loop.DailyAt(timeOfDay, () =>
        {
            Interlocked.Increment(ref fired);
            return default;
        });

        loop.Start();
        await Task.Delay(1200);
        await loop.StopAsync();

        Assert.That(fired, Is.EqualTo(1));   // fires once — next occurrence is tomorrow, so no refire
    }

    [Test]
    public async Task StopAsync_with_canceled_ct_abandons_wait()
    {
        var block = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(10) });
        loop.Every(TimeSpan.FromMilliseconds(10), async _ =>
        {
            entered.TrySetResult(true);
            await block.Task;   // job hangs — the ct makes the wait give up
        });

        loop.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(() => loop.StopAsync(cts.Token));

        block.TrySetResult(true);          // release the job — the loop got the cancel signal and exits on its own
        await loop.StopAsync();            // re-calling without a ct completes with a normal wait
    }
}
