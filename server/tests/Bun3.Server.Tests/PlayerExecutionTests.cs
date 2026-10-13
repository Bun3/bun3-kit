using Bun3.Server.Players;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Tests.PlayersProtocol;
using NUnit.Framework;
using static Bun3.Server.Tests.Helpers.PlayerExecutionHarness;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerExecutionTests
{
    private static readonly TimeSpan Timeout = PlayerExecutionHarness.Timeout;

    [TestCase(true)]
    [TestCase(false)]
    public async Task Direct_login_and_close_cannot_leave_a_connected_orphan(bool closeBeforeBinding)
    {
        await using var h = new PlayerExecutionHarness();
        var entered = Barrier();
        var resume = Barrier();
        var attachedCalls = 0;
        ExecutionPlayer? loaded = null;
        if (closeBeforeBinding) h.BeforeLoad = async () => { entered.SetResult(true); await resume.Task; };
        h.ConfigurePlayer = p =>
        {
            loaded = p;
            p.Attached = async () =>
            {
                attachedCalls++;
                if (!closeBeforeBinding) { entered.SetResult(true); await resume.Task; }
            };
        };
        await h.StartAsync();
        var connection = h.Transport.Connect(1);
        var session = h.Server.Sessions.Single();
        var login = session.SignInAsync("guest:a").AsTask();
        await entered.Task.WaitAsync(Timeout);
        try
        {
            connection.Close();
            if (closeBeforeBinding) await session.Closed.Task.WaitAsync(Timeout);
            else Assert.That(session.Closed.Task.IsCompleted, Is.False, "detach waits for the attached hook's ownership");
        }
        finally { resume.TrySetResult(true); }
        if (closeBeforeBinding)
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await login.WaitAsync(Timeout));
            Assert.That(attachedCalls, Is.Zero);
            Assert.That(h.Registry.TryGet("guest:a"), Is.Null);
            Assert.That(loaded!.ReleaseCalls, Is.EqualTo(1));
            Assert.That(loaded.RetirementCalls, Is.Zero);
        }
        else
        {
            await login.WaitAsync(Timeout);
            await session.Closed.Task.WaitAsync(Timeout);
            Assert.That(h.Registry.TryGet("guest:a"), Is.SameAs(loaded));
            Assert.That(loaded!.IsConnected, Is.False);
        }
        Assert.That(session.IsAuthenticated, Is.False);
        Assert.That(loaded!.CurrentSession, Is.Null);
        loaded.Attached = null;
        h.BeforeLoad = null;
        h.ConfigurePlayer = null;
        var reply = await LoginAsync(h.Transport.Connect(2));
        Assert.That(reply.Status, Is.Zero);
        Assert.That(reply.Login.IsReconnect, Is.EqualTo(!closeBeforeBinding));
    }
    [Test]
    public async Task Closed_duplicate_login_preserves_the_existing_binding()
    {
        await using var h = new PlayerExecutionHarness();
        await h.StartAsync();
        var oldConnection = h.Transport.Connect(1);
        await LoginAsync(oldConnection);
        var oldSession = h.Server.Sessions.Single();
        var player = oldSession.Player!;
        var ownership = await h.Registry.AcquireAsync("guest:a");
        Task<SignInResult<ExecutionPlayer>> login;
        try
        {
            var newConnection = h.Transport.Connect(2);
            var newSession = h.Server.Sessions.Single(s => s.Id == 2);
            login = newSession.SignInAsync("guest:a").AsTask();
            Assert.That(login.IsCompleted, Is.False);
            newConnection.Close();
            await newSession.Closed.Task.WaitAsync(Timeout);
        }
        finally { ownership.Dispose(); }
        Assert.ThrowsAsync<InvalidOperationException>(async () => await login.WaitAsync(Timeout));
        Assert.That(oldConnection.IsOpen, Is.True);
        Assert.That(oldSession.Player, Is.SameAs(player));
        Assert.That(player.CurrentSession, Is.SameAs(oldSession));
        Assert.That(h.Registry.TryGet("guest:a"), Is.SameAs(player));
        Assert.That(player.ReleaseCalls, Is.Zero);
    }

    [Test]
    public async Task Login_reply_reads_state_before_waiting_operation_runs()
    {
        await using var h = new PlayerExecutionHarness();
        var entered = Barrier();
        var release = Barrier();
        var retiring = Barrier();
        var finishRetirement = Barrier();
        h.ConfigurePlayer = p => p.Retiring = async () =>
        {
            p.Gold = 777;
            retiring.SetResult(true);
            await finishRetirement.Task;
        };
        h.AfterLogin = async _ => { entered.SetResult(true); await release.Task; };
        await h.StartAsync();
        var login = LoginAsync(h.Transport.Connect(1));
        await entered.Task.WaitAsync(Timeout);
        var retire = h.Registry.RetireAllAsync().AsTask();
        try
        {
            try
            {
                Assert.That(retire.IsCompleted, Is.False, "login handler owns the player until its reply exists");
                Assert.That(h.Registry.TryGet("guest:a")!.RetirementCalls, Is.Zero);
            }
            finally { release.TrySetResult(true); }
            var response = await login.WaitAsync(Timeout);
            Assert.That(response.Login.Gold, Is.EqualTo(100));
            await retiring.Task.WaitAsync(Timeout);
        }
        finally { finishRetirement.TrySetResult(true); }
        await retire.WaitAsync(Timeout);
    }

    [Test]
    public async Task Duplicate_login_waits_for_old_handler()
    {
        await using var h = new PlayerExecutionHarness();
        await h.StartAsync();
        var first = h.Transport.Connect(1);
        await LoginAsync(first);
        var oldSession = h.Server.Sessions.Single();
        var entered = Barrier();
        var release = Barrier();
        h.Handle = async _ => { entered.SetResult(true); await release.Task; };
        first.ReceivePacket(PacketTestHelper.Wrap(Bun3.Server.Rpc.Channels.Request,
            new PlayersRequest { RequestId = 2, AddGold = new AddGoldRequest { Amount = 7 } }));
        await entered.Task.WaitAsync(Timeout);
        var posted = 0;
        oldSession.Post(() => { posted++; return default; });
        var second = h.Transport.Connect(2);
        var login = LoginAsync(second);
        try
        {
            await Task.Delay(50);
            Assert.That(login.IsCompleted, Is.False);
            Assert.That(oldSession.Player, Is.Not.Null);
        }
        finally { release.TrySetResult(true); }
        var response = await login.WaitAsync(Timeout);
        Assert.That(response.Login.Gold, Is.EqualTo(107));
        Assert.That(posted, Is.Zero);
        Assert.That(oldSession.Player, Is.Null);
    }

    [Test]
    public async Task Canceled_multi_acquire_releases_prefix_and_preserves_waiters()
    {
        var table = new PlayerExecutionTable();
        table.RetainKey("a");
        using var b = await table.AcquireAsync("b", default);
        using var ct = new CancellationTokenSource();
        var multi = table.AcquireAsync(new[] { "a", "b" }, ct.Token).AsTask();
        var bWaiter = table.AcquireAsync("b", default).AsTask();
        var aWaiter = table.AcquireAsync("a", default).AsTask();
        table.ReleaseKey("a");
        ct.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await multi);
        using var a = await aWaiter.WaitAsync(Timeout);
        var next = table.AcquireAsync("a", default).AsTask();
        Assert.That(next.IsCompleted, Is.False);
        a.Dispose();
        using var nextOwner = await next.WaitAsync(Timeout);
        a.Dispose();
        var final = table.AcquireAsync("a", default).AsTask();
        Assert.That(final.IsCompleted, Is.False, "copied old scope cannot release the new owner");
        nextOwner.Dispose();
        using var last = await final.WaitAsync(Timeout);
        b.Dispose();
        using var bAgain = await bWaiter.WaitAsync(Timeout);
        var bLast = table.AcquireAsync("b", default).AsTask();
        Assert.That(bLast.IsCompleted, Is.False);
        bAgain.Dispose();
        using var bFinal = await bLast.WaitAsync(Timeout);
    }

    [Test]
    public async Task Reentrant_lifecycle_fails_without_deadlock()
    {
        await using var h = new PlayerExecutionHarness();
        var guarded = 0;
        h.ConfigurePlayer = p => p.Attached = async () =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await h.Registry.RetireAllAsync());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await ((ExecutionSession)p.CurrentSession!).SignInAsync("guest:b"));
            Assert.Throws<InvalidOperationException>(() => PlayerExecutionLane.ThrowIfOwned());
            guarded++;
            await Task.Yield();
        };
        await h.StartAsync();
        var connection = h.Transport.Connect(1);
        await LoginAsync(connection);
        var done = Barrier();
        h.Server.Sessions.Single().Post(async () =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await h.Registry.RetireAllAsync());
            Assert.ThrowsAsync<InvalidOperationException>(async () => await h.Server.Sessions.Single().SignInAsync("guest:b"));
            guarded++;
            done.SetResult(true);
        });
        await done.Task.WaitAsync(Timeout);
        var response = await SendAsync(connection, new PlayersRequest { RequestId = 2, GetGold = new GetGoldRequest() });
        Assert.That(response.Status, Is.Zero);
        Assert.That(guarded, Is.EqualTo(2));
    }

    [Test]
    public async Task Posted_tick_and_detach_share_player_ownership()
    {
        await using var h = new PlayerExecutionHarness();
        await h.StartAsync();
        var connection = h.Transport.Connect(1);
        await LoginAsync(connection);
        var session = h.Server.Sessions.Single();
        var player = session.Player!;
        var entered = Barrier();
        var release = Barrier();
        var detached = Barrier();
        var active = 0;
        var maximum = 0;
        player.Detached = () =>
        {
            maximum = Math.Max(maximum, ++active);
            active--;
            detached.SetResult(true);
            return default;
        };
        session.Post(async () =>
        {
            maximum = Math.Max(maximum, ++active);
            entered.SetResult(true);
            await release.Task;
            active--;
        });
        await entered.Task.WaitAsync(Timeout);
        var ticker = new PlayerTicker<ExecutionPlayer>(h.Registry);
        await ticker.TickAsync(TimeSpan.Zero);
        connection.Close();
        try { Assert.That(detached.Task.IsCompleted, Is.False); }
        finally { release.TrySetResult(true); }
        await detached.Task.WaitAsync(Timeout);
        Assert.That(maximum, Is.EqualTo(1));
        Assert.That(player.SaveCalls, Is.Zero);
        var oldTick = player.TickWork!;
        await LoginAsync(h.Transport.Connect(2));
        var completed = Barrier();
        h.Server.Sessions.Single().Post(async () => { await oldTick(); completed.SetResult(true); });
        await completed.Task.WaitAsync(Timeout);
        Assert.That(player.SaveCalls, Is.Zero, "a stale tick cannot save through the new binding");
    }

    [Test]
    public async Task Failed_legacy_save_discards_player_without_retirement_save()
    {
        await using var h = new PlayerExecutionHarness();
        await h.StartAsync();
        var connection = h.Transport.Connect(1);
        await LoginAsync(connection);
        var player = h.Registry.TryGet("guest:a")!;
        player.FailSave = true;
        player.MarkDirty();
        connection.Close();
        var deadline = DateTime.UtcNow + Timeout;
        while (player.ReleaseCalls == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.That(h.Registry.TryGet("guest:a"), Is.Null);
        Assert.That(player.SaveCalls, Is.EqualTo(1));
        Assert.That(player.RetirementCalls, Is.Zero);
        await LoginAsync(h.Transport.Connect(2));
        Assert.That(h.Loads, Is.EqualTo(2));
        Assert.That(h.Registry.TryGet("guest:a"), Is.Not.SameAs(player));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mutating_attach_or_detach_hook_failure_invalidates_player(bool attach)
    {
        await using var h = new PlayerExecutionHarness();
        ExecutionPlayer? failed = null;
        h.ConfigurePlayer = p =>
        {
            failed = p;
            Func<ValueTask> fail = () => { p.Gold++; p.MarkDirty(); throw new InvalidOperationException("hook failed"); };
            if (attach) p.Attached = fail; else p.Detached = fail;
        };
        await h.StartAsync();
        var connection = h.Transport.Connect(1);
        // An invalidating attach closes the transport, so no successful reply is promised.
        if (attach)
        {
            connection.ReceivePacket(PacketTestHelper.Wrap(Bun3.Server.Rpc.Channels.Request,
                new PlayersRequest { RequestId = 1, Login = new LoginRequest { DeviceId = "a" } }));
        }
        else { await LoginAsync(connection); connection.Close(); }
        var deadline = DateTime.UtcNow + Timeout;
        while ((failed == null || failed.ReleaseCalls == 0) && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.That(h.Registry.TryGet("guest:a"), Is.Null);
        Assert.That(failed!.RetirementCalls, Is.Zero);
        Assert.That(failed.SaveCalls, Is.Zero);
        Assert.That(failed.ReleaseCalls, Is.EqualTo(1));
        h.ConfigurePlayer = null;
        await LoginAsync(h.Transport.Connect(2));
        Assert.That(h.Registry.TryGet("guest:a"), Is.Not.SameAs(failed));
        Assert.That(h.Loads, Is.EqualTo(2));
    }
}
