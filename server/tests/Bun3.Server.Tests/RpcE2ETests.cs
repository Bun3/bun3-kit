using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class RpcE2ETests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class E2ESession : RpcSession
    {
        public E2ESession(IConnection connection) : base(connection) { }

        protected override ValueTask OnSessionOpenedAsync() =>
            SendUpdateAsync(new BroadcastedUpdate { Text = "welcome" });
    }

    private static RpcConfig<E2ESession> Config()
    {
        var config = new RpcConfig<E2ESession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 777 }));
        config.OnRequest<BuyItemRequest, BuyItemResponse>((s, req) =>
            req.ItemId == 1
                ? new ValueTask<Reply<BuyItemResponse>>(Reply.Fail(-1001))
                : new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse { RemainingGold = 1000 + req.ItemId }));
        return config;
    }

    private static async Task<(RpcServer<E2ESession, Request, Response, Update> server, TcpTransportListener listener)>
        StartServerAsync()
    {
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        var server = new RpcServer<E2ESession, Request, Response, Update>(
            listener, conn => new E2ESession(conn), Config());
        await server.StartAsync();
        return (server, listener);
    }

    private static ValueTask<RpcClient<Request, Response, Update>> ConnectClientAsync(
        TcpTransportListener listener, RpcClientOptions? options = null)
    {
        var connector = new TcpConnector(new TcpConnectorOptions
        {
            Host = "127.0.0.1",
            Port = listener.BoundPort!.Value,
        });
        return RpcClient<Request, Response, Update>.ConnectAsync(connector, options);
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
            var connector = new TcpConnector(new TcpConnectorOptions
            {
                Host = "127.0.0.1",
                Port = listener.BoundPort!.Value,
            });
            var client = await RpcClient<Request, Response, Update>.ConnectAsync(
                connector,
                configure: c => c.OnUpdate<BroadcastedUpdate>(u => received.TrySetResult(u.Text)));

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
        try
        {
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
        finally
        {
            await server.StopAsync();
        }
    }
}
