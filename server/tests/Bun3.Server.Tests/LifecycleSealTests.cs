using Bun3.Server.Abstractions;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Ticking;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class LifecycleSealTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class SealPlayer : Player
    {
        public int SaveCalls;
        public volatile bool MarkDirtyDuringNextSave;
        public int RetireCalls;

        protected override ValueTask OnSaveAsync()
        {
            Interlocked.Increment(ref SaveCalls);
            if (MarkDirtyDuringNextSave)
            {
                MarkDirtyDuringNextSave = false;
                MarkDirty();   // change arriving mid-save — without a version counter, the clear would erase it
            }
            return default;
        }

        protected override ValueTask OnRetiredAsync()
        {
            Interlocked.Increment(ref RetireCalls);
            return default;
        }
    }

    private sealed class SealSession : PlayerSession<SealPlayer>
    {
        public SealSession(IConnection connection) : base(connection) { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public PlayerRegistry<SealPlayer> Registry = null!;
        public RpcServer<SealSession, PlayersRequest, PlayersResponse, PlayersUpdate> Server = null!;
        public TcpTransportListener Listener = null!;
        public TickLoop? Loop;

        public static async Task<Harness> StartAsync(
            PlayersOptions? playersOptions = null, bool withTicker = false,
            Func<string, ValueTask<SealPlayer>>? loader = null)
        {
            var h = new Harness();
            var options = playersOptions ?? new PlayersOptions();
            h.Registry = new PlayerRegistry<SealPlayer>(
                loader ?? (_ => new ValueTask<SealPlayer>(new SealPlayer())), options);

            var config = new PlayersConfig<SealSession>();
            config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
            {
                var result = await s.SignInAsync($"guest:{req.DeviceId}");
                return new LoginResponse { Gold = 0, IsReconnect = result.IsReconnect };
            });
            config.OnRequest<AddGoldRequest, AddGoldResponse>((s, req) =>
            {
                s.Player!.MarkDirty();
                return new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse { Gold = 0 });
            });
            config.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
                new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = 0 }));

            h.Listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
            h.Server = new RpcServer<SealSession, PlayersRequest, PlayersResponse, PlayersUpdate>(
                h.Listener, h.Registry.Wrap(config, conn => new SealSession(conn)), config.Rpc);
            await h.Server.StartAsync();

            if (withTicker)
            {
                h.Loop = new TickLoop(new TickingOptions { TickInterval = TimeSpan.FromMilliseconds(20) });
                new PlayerTicker<SealPlayer>(h.Registry, options).Register(h.Loop);
                h.Loop.Start();
            }
            return h;
        }

        public async Task<(RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate> Client, SealSession Session)>
            ConnectAsync()
        {
            var client = await RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = Listener.BoundPort!.Value }));
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                foreach (var session in Server.Sessions)
                {
                    return (client, session);
                }
                await Task.Delay(10);
            }
            throw new TimeoutException("session was not created");
        }

        public async ValueTask DisposeAsync()
        {
            if (Loop != null) await Loop.StopAsync();
            await Server.StopAsync();
            await Registry.RetireAllAsync();
            Registry.Dispose();
        }
    }

    [Test]
    public async Task Concurrent_signin_exactly_one_wins()
    {
        await using var h = await Harness.StartAsync();
        var (client, session) = await h.ConnectAsync();

        // concurrent SignIn on the same session from outside handlers (two parallel Tasks) —
        // the CAS guard lets exactly one through
        var first = Task.Run(() => session.SignInAsync("guest:race").AsTask());
        var second = Task.Run(() => session.SignInAsync("guest:race").AsTask());

        var results = await Task.WhenAll(WrapAsync(first), WrapAsync(second));
        Assert.That(results.Count(r => r == null), Is.EqualTo(1), "exactly one succeeds");
        Assert.That(results.Count(r => r is InvalidOperationException), Is.EqualTo(1), "the other gets InvalidOperationException");
        client.Close();

        static async Task<Exception?> WrapAsync(Task task)
        {
            try { await task; return null; }
            catch (Exception ex) { return ex; }
        }
    }

    [Test]
    public async Task Retired_registry_rejects_late_signin()
    {
        await using var h = await Harness.StartAsync();
        var (client, _) = await h.ConnectAsync();

        await h.Registry.RetireAllAsync();

        // login attempt after retirement — the handler's SignInAsync throws, surfacing as status 2
        var reply = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "late" })
            .AsTask().WaitAsync(Timeout);
        Assert.That(reply.Status, Is.EqualTo(RpcStatus.HandlerException));
        Assert.That(h.Registry.TryGet("guest:late"), Is.Null, "a retired registry must not grow a new entry");
        client.Close();
    }

    [Test]
    public async Task MarkDirty_during_save_survives_to_next_sweep()
    {
        await using var h = await Harness.StartAsync(new PlayersOptions
        {
            PlayerTickInterval = TimeSpan.FromMilliseconds(40),
            SaveInterval = TimeSpan.FromMilliseconds(120),
        }, withTicker: true);
        var (client, _) = await h.ConnectAsync();
        var login = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "d" })
            .AsTask().WaitAsync(Timeout);
        Assert.That(login.IsOk, Is.True);
        var player = h.Registry.TryGet("guest:d")!;

        player.MarkDirtyDuringNextSave = true;
        await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 1 }).AsTask().WaitAsync(Timeout);

        var deadline = DateTime.UtcNow + Timeout;
        while ((player.SaveCalls < 2 || player.IsDirty) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        // the MarkDirty that arrived during the first save survived and triggered a second save (proves the version counter)
        Assert.That(player.SaveCalls, Is.GreaterThanOrEqualTo(2));
        Assert.That(player.IsDirty, Is.False, "clean after the second save");
        client.Close();
    }

    [Test]
    public async Task Dispose_stops_sweep_and_is_idempotent()
    {
        await using var h = await Harness.StartAsync(new PlayersOptions
        {
            GracePeriod = TimeSpan.FromMilliseconds(100),
        });
        var (client, _) = await h.ConnectAsync();
        var login = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "sweep" })
            .AsTask().WaitAsync(Timeout);
        Assert.That(login.IsOk, Is.True);
        var player = h.Registry.TryGet("guest:sweep")!;

        h.Registry.Dispose();
        h.Registry.Dispose();   // idempotent

        client.Close();          // detach — enters grace
        await Task.Delay(500);   // wait 5x the grace period (100ms)

        // Dispose is not retirement — the sweep stopped, so grace expiry causes no retirement
        Assert.That(player.RetireCalls, Is.Zero);
        Assert.That(h.Registry.TryGet("guest:sweep"), Is.Not.Null);
    }

    [Test]
    public async Task Retire_during_slow_load_leaves_no_orphan_entry()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var h = await Harness.StartAsync(loader: async _ =>
        {
            await gate.Task;
            return new SealPlayer();
        });
        var (client, _) = await h.ConnectAsync();

        // fire the login — the loader blocks on the gate, so SignInAsync stalls holding the accountKey stripe
        var loginTask = client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "slow" }).AsTask();
        await Task.Delay(100);   // make sure the loader is blocked

        // at RetireAll time there is no entry yet, so the snapshot misses it — completes without stripe contention
        await h.Registry.RetireAllAsync();

        gate.TrySetResult(true);   // release the loader — the pre-insert recheck must see _retired and block it
        var reply = await loginTask.WaitAsync(Timeout);

        Assert.That(reply.Status, Is.EqualTo(RpcStatus.HandlerException));
        Assert.That(h.Registry.TryGet("guest:slow"), Is.Null, "a slow-loaded new entry must not be orphaned during retirement");
        client.Close();
    }
}
