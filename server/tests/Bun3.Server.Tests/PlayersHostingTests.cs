using Bun3.Server.Abstractions;
using Bun3.Server.Hosting;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayersHostingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public sealed class HostPlayer : Player
    {
        public long Gold = 100;
        public readonly TaskCompletionSource<bool> Retired = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override ValueTask OnRetiredAsync()
        {
            Retired.TrySetResult(true);
            return default;
        }
    }

    public sealed class HostSession : PlayerSession<HostPlayer>
    {
        public HostSession(IConnection connection) : base(connection) { }
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddPlayerServer<HostSession, HostPlayer, PlayersRequest, PlayersResponse, PlayersUpdate>(
            loader: (sp, key) => new ValueTask<HostPlayer>(new HostPlayer()),
            configure: players =>
            {
                players.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
                {
                    var result = await s.SignInAsync($"guest:{req.DeviceId}");
                    return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
                });
                players.OnRequest<AddGoldRequest, AddGoldResponse>((s, req) =>
                {
                    s.Player!.Gold += req.Amount;
                    return new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse { Gold = s.Player.Gold });
                });
                players.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
                    new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = s.Player!.Gold }));
            },
            serverOptions: options => options.Port = 0);
        return builder.Build();
    }

    private static ValueTask<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> ConnectAsync(IHost host)
    {
        var port = host.Services.GetRequiredService<TcpTransportListener>().BoundPort!.Value;
        var connector = new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = port });
        return RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(connector);
    }

    [Test]
    public async Task Host_boots_gates_then_serves_login_roundtrip()
    {
        using var host = BuildHost();
        await host.StartAsync();
        try
        {
            var client = await ConnectAsync(host);

            var gated = await client.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
            Assert.That(gated.Status, Is.EqualTo(RpcStatus.Unauthenticated));

            var login = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "h" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login.IsOk, Is.True);
            Assert.That(login.Value!.Gold, Is.EqualTo(100));

            var gold = await client.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
            Assert.That(gold.Value!.Gold, Is.EqualTo(100));
            client.Close();
        }
        finally
        {
            await host.StopAsync().WaitAsync(Timeout);
        }
    }

    [Test]
    public async Task Host_stop_retires_all_players()
    {
        using var host = BuildHost();
        await host.StartAsync();
        var client = await ConnectAsync(host);
        var login = await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "h" })
            .AsTask().WaitAsync(Timeout);
        Assert.That(login.IsOk, Is.True);
        var registry = host.Services.GetRequiredService<PlayerRegistry<HostPlayer>>();
        var player = registry.TryGet("guest:h")!;

        await host.StopAsync().WaitAsync(Timeout);

        await player.Retired.Task.WaitAsync(Timeout);
        Assert.That(registry.Players, Is.Empty);
    }
}
