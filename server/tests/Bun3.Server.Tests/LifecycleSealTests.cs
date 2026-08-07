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
                MarkDirty();   // 저장 "중" 도착한 변경 — 버전 카운터가 없으면 클리어에 지워진다
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
            throw new TimeoutException("세션 미생성");
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

        // 핸들러 밖(두 병렬 Task)에서 같은 세션에 동시 SignIn — CAS 가드가 정확히 하나만 통과시킨다
        var first = Task.Run(() => session.SignInAsync("guest:race").AsTask());
        var second = Task.Run(() => session.SignInAsync("guest:race").AsTask());

        var results = await Task.WhenAll(WrapAsync(first), WrapAsync(second));
        Assert.That(results.Count(r => r == null), Is.EqualTo(1), "정확히 하나 성공");
        Assert.That(results.Count(r => r is InvalidOperationException), Is.EqualTo(1), "다른 하나는 InvalidOperationException");
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

        // 은퇴 후 로그인 시도 — 핸들러의 SignInAsync가 던져서 status 2로 표면화
        var reply = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "late" })
            .AsTask().WaitAsync(Timeout);
        Assert.That(reply.Status, Is.EqualTo(RpcStatus.HandlerException));
        Assert.That(h.Registry.TryGet("guest:late"), Is.Null, "은퇴한 레지스트리에 새 entry가 생기면 안 된다");
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
        while (player.SaveCalls < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        // 1차 저장 "중" 들어온 MarkDirty가 살아남아 2차 저장이 일어났다 (버전 카운터 증명)
        Assert.That(player.SaveCalls, Is.GreaterThanOrEqualTo(2));
        Assert.That(player.IsDirty, Is.False, "2차 저장 후 클린");
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
        h.Registry.Dispose();   // 멱등

        client.Close();          // detach — 유예 진입
        await Task.Delay(500);   // 유예(100ms)의 5배 대기

        // Dispose는 은퇴가 아니다 — 스윕이 멈췄으므로 유예가 만료돼도 은퇴가 일어나지 않는다
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

        // 로그인 발사 — 로더가 gate에서 블록되어 SignInAsync가 accountKey의 스트라이프를 쥔 채 멈춘다
        var loginTask = client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "slow" }).AsTask();
        await Task.Delay(100);   // 로더가 확실히 블록 중임을 보장

        // RetireAll 시점엔 entry가 아직 없어 스냅샷에 안 잡힌다 — 스트라이프 경합 없이 완료된다
        await h.Registry.RetireAllAsync();

        gate.TrySetResult(true);   // 로더 해제 — 삽입 직전 재확인이 _retired를 보고 막아야 한다
        var reply = await loginTask.WaitAsync(Timeout);

        Assert.That(reply.Status, Is.EqualTo(RpcStatus.HandlerException));
        Assert.That(h.Registry.TryGet("guest:slow"), Is.Null, "은퇴 중 느리게 로드된 신규 entry가 고아로 남으면 안 된다");
        client.Close();
    }
}
