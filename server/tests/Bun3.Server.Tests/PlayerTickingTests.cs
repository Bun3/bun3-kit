using System.Collections.Concurrent;
using Bun3.Server.Abstractions;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Ticking;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerTickingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class TickPlayer : Player
    {
        public long Gold = 100;
        public readonly ConcurrentQueue<TimeSpan> TickDeltas = new();
        public int SaveCalls;
        public volatile bool FailNextSave;
        private int _concurrent;
        public readonly ConcurrentQueue<string> Violations = new();

        protected override async ValueTask OnTickAsync(TimeSpan delta)
        {
            Enter("tick");
            TickDeltas.Enqueue(delta);
            await Task.Delay(1);
            Exit();
        }

        protected override ValueTask OnSaveAsync()
        {
            Interlocked.Increment(ref SaveCalls);
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("save-fail");
            }
            return default;
        }

        public void Enter(string who)
        {
            if (Interlocked.Increment(ref _concurrent) != 1)
            {
                Violations.Enqueue(who);
            }
        }

        public void Exit() => Interlocked.Decrement(ref _concurrent);
    }

    private sealed class TickSession : PlayerSession<TickPlayer>
    {
        public TickSession(IConnection connection) : base(connection) { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public PlayerRegistry<TickPlayer> Registry = null!;
        public RpcServer<TickSession, PlayersRequest, PlayersResponse, PlayersUpdate> Server = null!;
        public TcpTransportListener Listener = null!;
        public TickLoop Loop = null!;

        public static async Task<Harness> StartAsync(
            TimeSpan? tickInterval = null, TimeSpan? saveInterval = null)
        {
            var h = new Harness();
            var playersOptions = new PlayersOptions
            {
                PlayerTickInterval = tickInterval ?? TimeSpan.FromMilliseconds(40),
                SaveInterval = saveInterval ?? TimeSpan.FromMilliseconds(150),
            };
            h.Registry = new PlayerRegistry<TickPlayer>(
                _ => new ValueTask<TickPlayer>(new TickPlayer()), playersOptions);

            var config = new PlayersConfig<TickSession>();
            config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
            {
                var result = await s.SignInAsync($"guest:{req.DeviceId}");
                return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
            });
            config.OnRequest<AddGoldRequest, AddGoldResponse>(async (s, req) =>
            {
                var player = s.Player!;
                player.Enter("handler");
                player.Gold += req.Amount;
                player.MarkDirty();
                await Task.Delay(1);
                player.Exit();
                return new AddGoldResponse { Gold = player.Gold };
            });
            // The Request oneof also includes get_gold, so boot validation (RpcSchema.Validate)
            // rejects unregistered handlers — a stub is required.
            config.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
                new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = s.Player!.Gold }));

            h.Listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
            h.Server = new RpcServer<TickSession, PlayersRequest, PlayersResponse, PlayersUpdate>(
                h.Listener, h.Registry.Wrap(config, conn => new TickSession(conn)), config.Rpc);
            await h.Server.StartAsync();

            h.Loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
            new PlayerTicker<TickPlayer>(h.Registry, playersOptions).Register(h.Loop);
            h.Loop.Start();
            return h;
        }

        public ValueTask<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> ConnectAsync() =>
            RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = Listener.BoundPort!.Value }));

        public async Task<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> LoginAsync(string device)
        {
            var client = await ConnectAsync();
            var reply = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = device })
                .AsTask().WaitAsync(Timeout);
            Assert.That(reply.IsOk, Is.True);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await Loop.StopAsync();
            await Server.StopAsync();
            await Registry.RetireAllAsync();
        }
    }

    [Test]
    public async Task Tick_hook_runs_while_connected_with_sane_delta()
    {
        await using var h = await Harness.StartAsync();
        var client = await h.LoginAsync("t1");
        var player = h.Registry.TryGet("guest:t1")!;

        await Task.Delay(500);

        Assert.That(player.TickDeltas.Count, Is.GreaterThanOrEqualTo(3));
        foreach (var delta in player.TickDeltas)
        {
            Assert.That(delta, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(delta, Is.LessThan(TimeSpan.FromSeconds(1)));
        }
        Assert.That(player.Violations, Is.Empty);
        client.Close();
    }

    [Test]
    public async Task Tick_and_handlers_never_run_concurrently()
    {
        await using var h = await Harness.StartAsync(tickInterval: TimeSpan.FromMilliseconds(20));
        var client = await h.LoginAsync("t2");
        var player = h.Registry.TryGet("guest:t2")!;

        for (var i = 0; i < 50; i++)
        {
            var reply = await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 1 })
                .AsTask().WaitAsync(Timeout);
            Assert.That(reply.IsOk, Is.True);
        }

        Assert.That(player.Violations, Is.Empty, "tick hook and handlers must never run concurrently");
        Assert.That(player.TickDeltas.Count, Is.GreaterThanOrEqualTo(1));
        client.Close();
    }

    [Test]
    public async Task Ticks_pause_during_grace_and_resume_after_relogin()
    {
        await using var h = await Harness.StartAsync();
        var client1 = await h.LoginAsync("t3");
        var player = h.Registry.TryGet("guest:t3")!;
        await Task.Delay(150);

        client1.Close();
        await Task.Delay(200);                       // wait for detach propagation
        var countDuringGraceStart = player.TickDeltas.Count;
        await Task.Delay(500);                       // during grace — offline window
        Assert.That(player.TickDeltas.Count, Is.LessThanOrEqualTo(countDuringGraceStart + 1),
            "ticks must pause during grace (1-tick tolerance at the propagation boundary)");

        var client2 = await h.LoginAsync("t3");      // rebind
        await Task.Delay(300);
        Assert.That(player.TickDeltas.Count, Is.GreaterThan(countDuringGraceStart + 1), "ticks resume after relogin");
        // delta reset — the 500ms offline window was not added into delta
        foreach (var delta in player.TickDeltas)
        {
            Assert.That(delta, Is.LessThan(TimeSpan.FromMilliseconds(450)));
        }
        client2.Close();
    }

    [Test]
    public async Task Periodic_save_flushes_dirty_then_stays_quiet_when_clean()
    {
        await using var h = await Harness.StartAsync(saveInterval: TimeSpan.FromMilliseconds(150));
        var client = await h.LoginAsync("t4");
        var player = h.Registry.TryGet("guest:t4")!;

        await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 5 }).AsTask().WaitAsync(Timeout);
        await Task.Delay(600);
        Assert.That(player.SaveCalls, Is.GreaterThanOrEqualTo(1), "periodic save when dirty");
        Assert.That(player.IsDirty, Is.False, "dirty cleared on successful save");

        var saved = player.SaveCalls;
        await Task.Delay(500);
        Assert.That(player.SaveCalls, Is.EqualTo(saved), "no save when clean");
        client.Close();
    }

    [Test]
    public async Task Failed_save_keeps_dirty_and_retries_next_period()
    {
        await using var h = await Harness.StartAsync(saveInterval: TimeSpan.FromMilliseconds(150));
        var client = await h.LoginAsync("t5");
        var player = h.Registry.TryGet("guest:t5")!;

        player.FailNextSave = true;
        await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 5 }).AsTask().WaitAsync(Timeout);
        await Task.Delay(800);

        Assert.That(player.SaveCalls, Is.GreaterThanOrEqualTo(2), "must retry — dirty is kept after failure");
        Assert.That(player.IsDirty, Is.False, "clean after successful retry");
        client.Close();
    }

    [Test]
    public async Task Detach_saves_dirty_immediately()
    {
        // very long save interval — proves the save comes from the detach path, not the periodic sweep
        await using var h = await Harness.StartAsync(saveInterval: TimeSpan.FromSeconds(60));
        var client = await h.LoginAsync("t6");
        var player = h.Registry.TryGet("guest:t6")!;

        await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 5 }).AsTask().WaitAsync(Timeout);
        Assert.That(player.SaveCalls, Is.Zero);

        client.Close();
        var deadline = DateTime.UtcNow + Timeout;
        while (player.SaveCalls == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.That(player.SaveCalls, Is.EqualTo(1), "exactly one immediate save on detach");
        Assert.That(player.IsDirty, Is.False);
    }

    [Test]
    public async Task Duplicate_login_under_ticking_stays_consistent()
    {
        await using var h = await Harness.StartAsync();
        var client1 = await h.LoginAsync("t7");
        var player = h.Registry.TryGet("guest:t7")!;
        await Task.Delay(150);

        var client2 = await h.LoginAsync("t7");      // NewWins — client1 kicked
        await Task.Delay(400);

        Assert.That(player.Violations, Is.Empty, "no concurrent execution even during ownership transfer race");
        var before = player.TickDeltas.Count;
        await Task.Delay(300);
        Assert.That(player.TickDeltas.Count, Is.GreaterThan(before), "ticks continue on the new session");
        client2.Close();
        client1.Close();
    }
}
