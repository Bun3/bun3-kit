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
            // 배포 편차: players_game.proto의 Request oneof에는 get_gold도 있어 부트 검증
            // (RpcSchema.Validate)이 미등록 핸들러를 거부한다 — Task 2에서 확인된 동일 이슈.
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

        Assert.That(player.Violations, Is.Empty, "틱 훅과 핸들러가 동시에 실행되면 안 된다");
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
        await Task.Delay(200);                       // detach 전파 대기
        var countDuringGraceStart = player.TickDeltas.Count;
        await Task.Delay(500);                       // 유예 중 — 오프라인 구간
        Assert.That(player.TickDeltas.Count, Is.LessThanOrEqualTo(countDuringGraceStart + 1),
            "유예 중에는 틱이 멈춰야 한다 (전파 경계의 1회 오차 허용)");

        var client2 = await h.LoginAsync("t3");      // 재바인딩
        await Task.Delay(300);
        Assert.That(player.TickDeltas.Count, Is.GreaterThan(countDuringGraceStart + 1), "재접속 후 틱 재개");
        // delta 리셋 — 오프라인 500ms가 delta에 합산되지 않았다
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
        Assert.That(player.SaveCalls, Is.GreaterThanOrEqualTo(1), "dirty면 주기 저장");
        Assert.That(player.IsDirty, Is.False, "저장 성공 시 dirty 해제");

        var saved = player.SaveCalls;
        await Task.Delay(500);
        Assert.That(player.SaveCalls, Is.EqualTo(saved), "클린이면 저장하지 않는다");
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

        Assert.That(player.SaveCalls, Is.GreaterThanOrEqualTo(2), "실패 후 dirty 유지로 재시도되어야 한다");
        Assert.That(player.IsDirty, Is.False, "재시도 성공 후 클린");
        client.Close();
    }

    [Test]
    public async Task Detach_saves_dirty_immediately()
    {
        // 저장 주기를 아주 길게 — 주기 스윕이 아니라 detach 경로의 저장임을 보장
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

        Assert.That(player.SaveCalls, Is.EqualTo(1), "detach 시 즉시 저장 1회");
        Assert.That(player.IsDirty, Is.False);
    }

    [Test]
    public async Task Duplicate_login_under_ticking_stays_consistent()
    {
        await using var h = await Harness.StartAsync();
        var client1 = await h.LoginAsync("t7");
        var player = h.Registry.TryGet("guest:t7")!;
        await Task.Delay(150);

        var client2 = await h.LoginAsync("t7");      // NewWins — client1 킥
        await Task.Delay(400);

        Assert.That(player.Violations, Is.Empty, "소유권 이전 경합에서도 동시 실행 금지 유지");
        var before = player.TickDeltas.Count;
        await Task.Delay(300);
        Assert.That(player.TickDeltas.Count, Is.GreaterThan(before), "새 세션에서 틱 계속");
        client2.Close();
        client1.Close();
    }
}
