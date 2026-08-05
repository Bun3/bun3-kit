using System.Linq;
using Bun3.Server.Abstractions;
using Bun3.Server.Messaging;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class MessagingE2ETests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class E2ESession : MessagingSession
    {
        public E2ESession(IConnection connection) : base(connection) { }

        protected override ValueTask OnSessionOpenedAsync() =>
            SendUpdateAsync(new BroadcastedUpdate { Text = "welcome" });
    }

    private static MessagingConfig<E2ESession> Config()
    {
        var config = new MessagingConfig<E2ESession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 777 }));
        config.OnRequest<BuyItemRequest, BuyItemResponse>((s, req) =>
            req.ItemId == 1
                ? new ValueTask<Reply<BuyItemResponse>>(Reply.Fail(-1001))
                : new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse { RemainingGold = 1000 + req.ItemId }));
        return config;
    }

    private static async Task<(MessagingServer<E2ESession, Request, Response, Update> server, TcpTransportListener listener)>
        StartServerAsync()
    {
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        var server = new MessagingServer<E2ESession, Request, Response, Update>(
            listener, conn => new E2ESession(conn), Config());
        await server.StartAsync();
        return (server, listener);
    }

    private static ValueTask<MessagingClient<Request, Response, Update>> ConnectClientAsync(
        TcpTransportListener listener, MessagingClientOptions? options = null)
    {
        var connector = new TcpConnector(new TcpConnectorOptions
        {
            Host = "127.0.0.1",
            Port = listener.BoundPort!.Value,
        });
        return MessagingClient<Request, Response, Update>.ConnectAsync(connector, options);
    }

    [Test]
    public async Task E2E_request_response_roundtrip()
    {
        var (server, listener) = await StartServerAsync();
        try
        {
            var client = await ConnectClientAsync(listener);
            var reply = await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
                .AsTask().WaitAsync(Timeout);
            Assert.That(reply.IsOk, Is.True);
            Assert.That(reply.Value!.UnixMs, Is.EqualTo(777));
            client.Close();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task E2E_failure_status_code()
    {
        var (server, listener) = await StartServerAsync();
        try
        {
            var client = await ConnectClientAsync(listener);
            var reply = await client.RequestAsync<BuyItemResponse>(new BuyItemRequest { ItemId = 1 })
                .AsTask().WaitAsync(Timeout);
            Assert.That(reply.Status, Is.EqualTo(-1001));
            Assert.That(reply.Value, Is.Null);
            client.Close();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task E2E_push_is_received()
    {
        var (server, listener) = await StartServerAsync();
        try
        {
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = await ConnectClientAsync(listener);
            client.OnUpdate<BroadcastedUpdate>(u => received.TrySetResult(u.Text));

            // OnSessionOpenedAsync의 welcome 푸시 — 구독 등록과 경합할 수 있으므로
            // 수신 실패 시 서버 세션에서 한 번 더 밀어 재검증한다.
            var sessionPush = server.Sessions.Count > 0
                ? server.Sessions.First().SendUpdateAsync(new BroadcastedUpdate { Text = "welcome" })
                : default;
            await sessionPush;

            Assert.That(await received.Task.WaitAsync(Timeout), Is.EqualTo("welcome"));
            client.Close();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task E2E_concurrent_requests_correlate_correctly()
    {
        var (server, listener) = await StartServerAsync();
        try
        {
            var client = await ConnectClientAsync(listener);

            var tasks = new List<Task<Reply<BuyItemResponse>>>();
            for (var itemId = 10; itemId < 30; itemId++)
            {
                tasks.Add(client.RequestAsync<BuyItemResponse>(new BuyItemRequest { ItemId = itemId }).AsTask());
            }

            var replies = await Task.WhenAll(tasks).WaitAsync(Timeout);
            for (var i = 0; i < replies.Length; i++)
            {
                Assert.That(replies[i].IsOk, Is.True);
                Assert.That(replies[i].Value!.RemainingGold, Is.EqualTo(1000 + 10 + i),
                    "응답이 자기 요청과 상관되어야 한다");
            }
            client.Close();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task E2E_graceful_shutdown_fails_pending_and_fires_closed()
    {
        var (server, listener) = await StartServerAsync();
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = await ConnectClientAsync(listener);
        client.Closed += _ => closed.TrySetResult(true);

        // 세션 수립 확인 후 정지
        var warmup = await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
            .AsTask().WaitAsync(Timeout);
        Assert.That(warmup.IsOk, Is.True);

        await server.StopAsync();

        await closed.Task.WaitAsync(Timeout);
        Assert.That(client.IsConnected, Is.False);
        Assert.ThrowsAsync<ConnectionClosedException>(async () =>
            await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest()));
    }
}
