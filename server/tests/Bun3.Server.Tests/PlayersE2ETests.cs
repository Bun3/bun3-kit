using Bun3.Server.Abstractions;
using Bun3.Server.Auth;
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
                throw new InvalidOperationException(auth.Error);   // 이 E2E에 실패 경로 없음 — 방어만

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

            // ⓪ 미인증 게이트
            var client1 = await Connect();
            var gated = await client1.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
            Assert.That(gated.Status, Is.EqualTo(RpcStatus.Unauthenticated));

            // ① 게스트 로그인
            var login1 = await client1.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "e2e" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login1.Value!.IsReconnect, Is.False);
            Assert.That(login1.Value.Gold, Is.EqualTo(100));

            // ② Player 상태를 쓰는 요청
            var added = await client1.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = 23 })
                .AsTask().WaitAsync(Timeout);
            Assert.That(added.Value!.Gold, Is.EqualTo(123));

            // ③ 강제 절단
            client1.Close();

            // ④ 유예(기본 60초) 내 재접속 — 상태 그대로, 로더 재호출 없음
            var client2 = await Connect();
            var login2 = await client2.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "e2e" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login2.Value!.IsReconnect, Is.True);
            Assert.That(login2.Value.Gold, Is.EqualTo(123));
            Assert.That(loaderCalls, Is.EqualTo(1));

            // ⑤ 같은 계정 두 번째 클라 → 기존 클라 킥
            var client2Closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            client2.Closed += _ => client2Closed.TrySetResult(true);
            var client3 = await Connect();
            var login3 = await client3.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "e2e" })
                .AsTask().WaitAsync(Timeout);
            Assert.That(login3.Value!.IsReconnect, Is.True);
            Assert.That(login3.Value.Gold, Is.EqualTo(123));
            await client2Closed.Task.WaitAsync(Timeout);
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
