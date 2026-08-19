using Bun3.Server.Abstractions;
using Bun3.Server.Auth;
using Bun3.Server.Core;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayersE2ETests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class E2EPlayer : Player
    {
        public long Gold = 100;
    }

    private sealed class E2ESession : PlayerSession<E2EPlayer>
    {
        public E2ESession(IConnection connection) : base(connection) { }
    }

    [Test]
    public async Task Guest_login_vertical_slice()
    {
        var loaderCalls = 0;
        var registry = new PlayerRegistry<E2EPlayer>(key =>
        {
            Interlocked.Increment(ref loaderCalls);
            return new ValueTask<E2EPlayer>(new E2EPlayer());
        });
        var verifier = new GuestVerifier();
        var config = new PlayersConfig<E2ESession>();
        config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
        {
            var auth = await verifier.VerifyAsync(req.DeviceId);
            if (!auth.Succeeded)
                throw new InvalidOperationException(auth.Error);   // no failure path in this E2E — defensive only

            var result = await s.SignInAsync(auth.Identity.ToAccountKey());
            return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
        });
        config.OnRequest<AddGoldRequest, AddGoldResponse>((s, req) =>
        {
            s.Player!.Gold += req.Amount;
            return new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse { Gold = s.Player.Gold });
        });
        config.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
            new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = s.Player!.Gold }));

        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        var server = new RpcServer<E2ESession, PlayersRequest, PlayersResponse, PlayersUpdate>(
            listener, registry.Wrap(config, conn => new E2ESession(conn)), config.Rpc);
        await server.StartAsync();
        try
        {
            ValueTask<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> Connect() =>
                RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                    new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = listener.BoundPort!.Value }));

            // ⓪ unauthenticated gate
            var client1 = await Connect();
            var gated = await client1.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
            Assert.That(gated.Status, Is.EqualTo(RpcStatus.Unauthenticated));

            // ① guest login
            var login1 = await client1.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "e2e" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login1.Value!.IsReconnect, Is.False);
            Assert.That(login1.Value.Gold, Is.EqualTo(100));

            // ② request that writes Player state
            var added = await client1.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 23 })
                .AsTask().WaitAsync(Timeout);
            Assert.That(added.Value!.Gold, Is.EqualTo(123));

            // ③ forced disconnect
            client1.Close();

            // ④ reconnect within grace period (default 60s) — state intact, loader not called again
            var client2 = await Connect();
            var login2 = await client2.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "e2e" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login2.Value!.IsReconnect, Is.True);
            Assert.That(login2.Value.Gold, Is.EqualTo(123));
            Assert.That(loaderCalls, Is.EqualTo(1));

            // ⑤ second client on the same account -> existing client kicked
            var client2Closed = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            client2.Closed += info => client2Closed.TrySetResult(info);
            var client3 = await Connect();
            var login3 = await client3.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "e2e" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login3.Value!.IsReconnect, Is.True);
            Assert.That(login3.Value.Gold, Is.EqualTo(123));
            var kicked = await client2Closed.Task.WaitAsync(Timeout);
            Assert.That(kicked.Code, Is.EqualTo(DisconnectCode.DuplicateLogin));   // "logged in on another device" reason delivered
            Assert.That(loaderCalls, Is.EqualTo(1));
            client3.Close();
        }
        finally
        {
            await server.StopAsync();
            await registry.RetireAllAsync();
        }
    }
}
