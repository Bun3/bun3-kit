using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Tests.OperationsProtocol;
using NUnit.Framework;
namespace Bun3.Server.Tests;
[TestFixture]
public class PlayerOperationRpcTests
{
    private sealed class AdminRpcOperation : PlayerOperation<ExecutionPlayer, TransferResponse>
    {
        internal int ApplyCalls;
        internal AdminRpcOperation(string key) : base(Guid.NewGuid(), "grant",
            new PlayerOperationActor(PlayerOperationActorKind.Administrator, key), new[] { key }, new byte[32],
            new ProtobufPlayerResultCodec<TransferResponse>(TransferResponse.Parser)) { }
        public override int Validate(PlayerOperationContext<ExecutionPlayer> context) => 0;
        public override TransferResponse Apply(PlayerOperationContext<ExecutionPlayer> context) { ApplyCalls++; return new(); }
    }

    [Test]
    public async Task Registered_game_rpc_rejects_an_administrator_with_the_authenticated_key()
    {
        var store = new OperationStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var config = new PlayersConfig<ExecutionSession>();
        AdminRpcOperation? operation = null;
        config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) => new LoginResponse { Balance = (await s.SignInAsync(req.Key)).Player.Gold });
        config.OnRequest<GetBalanceRequest, GetBalanceResponse>((s, _) => new(new GetBalanceResponse { Balance = s.Player!.Gold }));
        config.OnPlayerOperation<ExecutionPlayer, TransferRequest, TransferResponse>((key, _) => operation = new AdminRpcOperation(key));
        var transport = new FakeTransport();
        var server = new RpcServer<ExecutionSession, OperationRequest, OperationResponse, OperationUpdate>(transport,
            registry.Wrap(config, c => new ExecutionSession(c)), config.Rpc);
        await server.StartAsync();
        try
        {
            var connection = transport.Connect(1);
            await PlayerOperationRpcHarness.Send(connection, new OperationRequest { Login = new LoginRequest { Key = "a" } });
            var result = await PlayerOperationRpcHarness.Send(connection, new OperationRequest { Transfer = new TransferRequest() });
            Assert.That(result.Status, Is.EqualTo(RpcStatus.InvalidOperation));
            Assert.That(operation!.ApplyCalls, Is.Zero);
            Assert.That(store.Receipts, Is.Empty);
        }
        finally { await server.StopAsync(); await registry.RetireAllAsync(); }
    }

    [Test]
    public async Task Operation_rpc_uses_current_envelope_on_replay()
    {
        await using var h = new PlayerOperationRpcHarness();
        await h.Start();
        var c = await h.Login(1, "a");
        var transfer = new TransferRequest { Id = Guid.NewGuid().ToString(), Target = "b" };
        var first = await PlayerOperationRpcHarness.Send(c, new OperationRequest { RequestId = 10, Transfer = transfer });
        var replay = await PlayerOperationRpcHarness.Send(c, new OperationRequest { RequestId = 11, Transfer = transfer });
        Assert.That((first.RequestId, replay.RequestId), Is.EqualTo((10, 11)));
        Assert.That(first.Status, Is.Zero);
        Assert.That(replay.Transfer, Is.EqualTo(first.Transfer));
        Assert.That(h.Store.Rows["a"].Gold, Is.EqualTo(90));
        Assert.That(h.Store.Receipts.Count, Is.EqualTo(1));
    }
    [Test]
    public void Missing_store_fails_at_wrap()
    {
        using var registry = new PlayerRegistry<ExecutionPlayer>(_ => new(new ExecutionPlayer()));
        var config = new PlayersConfig<ExecutionSession>();
        config.OnPlayerOperation<ExecutionPlayer, TransferRequest, TransferResponse>((actor, req) => new RpcTransfer(actor, req, null));
        Assert.Throws<InvalidOperationException>(() => registry.Wrap(config, c => new ExecutionSession(c)));
    }
    private sealed class OtherPlayer : Player { }
    [Test]
    public void Wrong_player_type_fails_at_wrap()
    {
        using var registry = new PlayerRegistry<ExecutionPlayer>(new OperationStore());
        var config = new PlayersConfig<ExecutionSession>();
        config.OnPlayerOperation<OtherPlayer, TransferRequest, TransferResponse>((_, _) => null!);
        Assert.Throws<InvalidOperationException>(() => registry.Wrap(config, c => new ExecutionSession(c)));
    }

    [Test]
    public async Task Raw_rpc_registration_still_acquires_player_owner()
    {
        await using var h = new PlayerOperationRpcHarness(); await h.Start();
        var c = await h.Login(1, "a");
        var entered = PlayerExecutionHarness.Barrier(); var release = PlayerExecutionHarness.Barrier();
        h.Reading = async _ => { entered.TrySetResult(true); await release.Task; };
        var reading = PlayerOperationRpcHarness.Send(c, new OperationRequest { RequestId = 2, GetBalance = new GetBalanceRequest() });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var operation = new TransferOperation();
        var applying = h.Registry.ExecuteAsync(operation).AsTask();
        try { Assert.That(operation.ApplyCalls, Is.Zero); Assert.That(applying.IsCompleted, Is.False); }
        finally { release.TrySetResult(true); }
        Assert.That((await reading).GetBalance.Balance, Is.EqualTo(100));
        Assert.That((await applying).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
    }

    [Test]
    public async Task Late_operation_registration_does_not_bypass_snapshot()
    {
        var store = new OperationStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var config = new PlayersConfig<ExecutionSession>();
        config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) => new LoginResponse { Balance = (await s.SignInAsync(req.Key)).Player.Gold });
        config.OnRequest<GetBalanceRequest, GetBalanceResponse>((s, _) => new(new GetBalanceResponse { Balance = s.Player!.Gold }));
        var factory = registry.Wrap(config, c => new ExecutionSession(c));
        config.OnPlayerOperation<ExecutionPlayer, TransferRequest, TransferResponse>((actor, req) => new RpcTransfer(actor, req, null));
        var transport = new FakeTransport();
        var server = new RpcServer<ExecutionSession, OperationRequest, OperationResponse, OperationUpdate>(transport, factory, config.Rpc);
        await server.StartAsync();
        try
        {
            var c = transport.Connect(1);
            await PlayerOperationRpcHarness.Send(c, new OperationRequest { Login = new LoginRequest { Key = "a" } });
            var result = await PlayerOperationRpcHarness.Send(c, new OperationRequest { Transfer = new TransferRequest { Id = Guid.NewGuid().ToString(), Target = "b" } });
            Assert.That(result.Status, Is.EqualTo(RpcStatus.InvalidOperation));
            Assert.That(store.Receipts, Is.Empty);
        }
        finally { await server.StopAsync(); await registry.RetireAllAsync(); }
    }
}
