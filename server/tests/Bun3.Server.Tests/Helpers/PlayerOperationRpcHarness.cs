using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.OperationsProtocol;
using NUnit.Framework;
using static Bun3.Server.Tests.Helpers.PacketTestHelper;
namespace Bun3.Server.Tests.Helpers;
internal sealed class RpcTransfer(string actor, TransferRequest request, Action<PlayerOperationContext<ExecutionPlayer>>? apply)
    : PlayerOperation<ExecutionPlayer, TransferResponse>(Guid.Parse(request.Id), "transfer", actor,
        new[] { actor, request.Target }, new byte[32], new ProtobufPlayerResultCodec<TransferResponse>(TransferResponse.Parser))
{
    public override int Validate(PlayerOperationContext<ExecutionPlayer> context) => 0;
    public override TransferResponse Apply(PlayerOperationContext<ExecutionPlayer> context)
    {
        context.GetPlayer(ActorKey).Gold -= 10;
        context.GetPlayer(request.Target).Gold += 10;
        apply?.Invoke(context);
        return new TransferResponse { Balance = context.GetPlayer(ActorKey).Gold };
    }
}
internal sealed class PlayerOperationRpcHarness : IAsyncDisposable
{
    internal readonly OperationStore Store = new();
    internal readonly FakeTransport Transport = new();
    internal readonly PlayerRegistry<ExecutionPlayer> Registry;
    internal readonly PlayersConfig<ExecutionSession> Config = new();
    internal readonly RpcServer<ExecutionSession, OperationRequest, OperationResponse, OperationUpdate> Server;
    internal Action<PlayerOperationContext<ExecutionPlayer>>? Applying;
    internal Func<ExecutionSession, ValueTask>? Reading;
    internal PlayerOperationRpcHarness(Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        Registry = new(Store, logger: logger);
        Config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
            new LoginResponse { Balance = (await s.SignInAsync(req.Key)).Player.Gold });
        Config.Rpc.OnRequest<GetBalanceRequest, GetBalanceResponse>(async (s, _) =>
        {
            if (Reading != null) await Reading(s);
            return new GetBalanceResponse { Balance = s.Player!.Gold };
        });
        Config.OnPlayerOperation<ExecutionPlayer, TransferRequest, TransferResponse>((actor, req) => new RpcTransfer(actor, req, Applying));
        Server = new(Transport, Registry.Wrap(Config, c => new ExecutionSession(c)), Config.Rpc);
    }
    internal Task Start() => Server.StartAsync();
    internal async Task<FakeConnection> Login(long id, string key)
    {
        var c = Transport.Connect(id);
        await Send(c, new OperationRequest { RequestId = id, Login = new LoginRequest { Key = key } });
        return c;
    }
    internal static async Task<OperationResponse> Send(FakeConnection c, OperationRequest request)
    {
        c.ReceivePacket(Wrap(Channels.Request, request));
        await c.SentSignal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(c.SentPackets.TryDequeue(out var packet), Is.True);
        return OperationResponse.Parser.ParseFrom(packet!.AsSpan(1).ToArray());
    }
    public async ValueTask DisposeAsync() { await Server.StopAsync(); await Registry.RetireAllAsync(); Registry.Dispose(); }
}
