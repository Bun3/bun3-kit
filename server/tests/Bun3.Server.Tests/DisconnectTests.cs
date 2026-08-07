using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class DisconnectTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const int GameBanCode = -7;

    private sealed class KickSession : RpcSession
    {
        public KickSession(IConnection connection) : base(connection) { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public RpcServer<KickSession, PlayersRequest, PlayersResponse, PlayersUpdate> Server = null!;
        public TcpTransportListener Listener = null!;
        public TaskCompletionSource<bool> BlockHandlers = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<Harness> StartAsync(
            TimeSpan? idleKick = null, int maxQueued = 256)
        {
            var h = new Harness();
            var config = new RpcConfig<KickSession>();
            // 기동 전수 검증 충족용 — GetGold는 게임 킥 트리거, 나머지는 스텁
            config.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
            {
                s.Kick(GameBanCode);
                s.Kick(-99);   // 이중 킥 — 사유는 최초 1회만 송신(멱등), 클라는 GameBanCode를 받아야 한다
                return new ValueTask<Reply<GetGoldResponse>>(Reply.Fail(GameBanCode));
            });
            config.OnRequest<LoginRequest, LoginResponse>((s, req) =>
                new ValueTask<Reply<LoginResponse>>(new LoginResponse { Gold = 0 }));
            config.OnRequest<AddGoldRequest, AddGoldResponse>(async (s, req) =>
            {
                await h.BlockHandlers.Task;   // 큐 초과 테스트용 블로커
                return new AddGoldResponse { Gold = 0 };
            });

            h.Listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
            h.Server = new RpcServer<KickSession, PlayersRequest, PlayersResponse, PlayersUpdate>(
                h.Listener, conn => new KickSession(conn), config,
                new RpcServerOptions { IdleKickTimeout = idleKick, MaxQueuedPackets = maxQueued });
            await h.Server.StartAsync();
            return h;
        }

        public ValueTask<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> ConnectAsync(
            TimeSpan? pingInterval = null) =>
            RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = Listener.BoundPort!.Value }),
                new RpcClientOptions { PingInterval = pingInterval });

        public async ValueTask DisposeAsync()
        {
            BlockHandlers.TrySetResult(true);
            await Server.StopAsync();
        }
    }

    private static TaskCompletionSource<DisconnectInfo> Watch(
        RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate> client)
    {
        var closed = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Closed += info => closed.TrySetResult(info);
        return closed;
    }

    [Test]
    public async Task Game_kick_delivers_negative_code()
    {
        await using var h = await Harness.StartAsync();
        using var client = await h.ConnectAsync();
        var closed = Watch(client);

        try
        {
            await client.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
        }
        catch (ConnectionClosedException)
        {
            // 응답 송신과 close가 경합 — 응답을 못 받아도 무방, 사유 전달이 검증 대상
        }

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(GameBanCode));
        Assert.That(info.HasReason, Is.True);
    }

    [Test]
    public async Task Voluntary_close_reports_code_zero()
    {
        await using var h = await Harness.StartAsync();
        using var client = await h.ConnectAsync();
        var closed = Watch(client);

        client.Close();

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(DisconnectCode.None));
        Assert.That(info.HasReason, Is.False);
    }

    [Test]
    public async Task Idle_kick_delivers_reason()
    {
        await using var h = await Harness.StartAsync(idleKick: TimeSpan.FromMilliseconds(150));
        using var client = await h.ConnectAsync(pingInterval: null);   // 핑 끔 — idle 유도
        var closed = Watch(client);

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(DisconnectCode.IdleKick));
    }

    [Test]
    public async Task Server_shutdown_delivers_reason()
    {
        var h = await Harness.StartAsync();
        using var client = await h.ConnectAsync();
        var closed = Watch(client);

        await h.Server.StopAsync();

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(DisconnectCode.ServerShutdown));
    }

    [Test]
    public async Task Queue_overflow_delivers_reason()
    {
        await using var h = await Harness.StartAsync(maxQueued: 3);
        using var client = await h.ConnectAsync();
        var closed = Watch(client);

        // 블로킹 핸들러 1개 + 큐 상한 초과까지 요청 폭주 (응답은 기대하지 않음)
        var floods = new List<Task>();
        for (var i = 0; i < 10; i++)
        {
            floods.Add(client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 1 }).AsTask());
        }

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(DisconnectCode.QueueOverflow));

        h.BlockHandlers.TrySetResult(true);
        foreach (var flood in floods)
        {
            try { await flood; } catch { /* ConnectionClosed/Timeout — 관전 처리만 */ }
        }
    }

    [Test]
    public async Task Dispose_closes_and_is_idempotent()
    {
        await using var h = await Harness.StartAsync();
        var client = await h.ConnectAsync();
        var closed = Watch(client);

        client.Dispose();
        client.Dispose();   // 멱등

        var info = await closed.Task.WaitAsync(Timeout);
        Assert.That(info.Code, Is.EqualTo(DisconnectCode.None));
        Assert.That(client.IsConnected, Is.False);
    }
}
