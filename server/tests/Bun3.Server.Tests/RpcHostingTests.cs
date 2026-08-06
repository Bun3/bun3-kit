using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Bun3.Server.Hosting;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class RpcHostingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public sealed class HostedSession : RpcSession
    {
        public HostedSession(IConnection connection) : base(connection) { }
    }

    [Test]
    public async Task Host_boots_and_serves_typed_request()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddRpcServer<HostedSession, Request, Response, Update>(
            rpc =>
            {
                rpc.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
                    (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 99 }));
                rpc.OnRequest<BuyItemRequest, BuyItemResponse>(
                    (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse { RemainingGold = 1 }));
            },
            serverOptions: options => options.Port = 0);
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var port = host.Services.GetRequiredService<TcpTransportListener>().BoundPort!.Value;
            var client = await RpcClient<Request, Response, Update>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = port }));

            var reply = await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
                .AsTask().WaitAsync(Timeout);
            Assert.That(reply.IsOk, Is.True);
            Assert.That(reply.Value!.UnixMs, Is.EqualTo(99));
            client.Close();
        }
        finally
        {
            await host.StopAsync().WaitAsync(Timeout);
        }
    }

    [Test]
    public async Task Incomplete_config_fails_host_start_with_full_error_list()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddRpcServer<HostedSession, Request, Response, Update>(
            rpc => { },   // 아무 핸들러도 등록하지 않음
            serverOptions: options => options.Port = 0);
        using var host = builder.Build();

        var ex = Assert.ThrowsAsync<RpcValidationException>(async () => await host.StartAsync())!;
        Assert.That(ex.Message, Does.Contain("get_server_time"));
        Assert.That(ex.Message, Does.Contain("buy_item"));
    }
}
