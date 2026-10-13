using Bun3.Server.Abstractions;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using NUnit.Framework;
using static Bun3.Server.Tests.Helpers.PacketTestHelper;

namespace Bun3.Server.Tests.Helpers;

internal sealed class ExecutionPlayer : Player
{
    internal long Gold = 100;
    internal int SaveCalls;
    internal int RetirementCalls;
    internal int ReleaseCalls;
    internal Func<ValueTask>? Attached;
    internal Func<ValueTask>? Detached;
    internal Func<ValueTask>? Retiring;
    internal Func<ValueTask>? Ticking;
    internal Func<ValueTask>? Releasing { get; set; }
    protected internal override ValueTask OnTickAsync(TimeSpan delta) => Ticking?.Invoke() ?? default;
    internal bool FailSave;
    protected internal override ValueTask OnAttachedAsync(bool reconnect) => Attached?.Invoke() ?? default;
    protected internal override ValueTask OnDetachedAsync() => Detached?.Invoke() ?? default;
    protected internal override ValueTask OnSaveAsync()
    {
        SaveCalls++;
        if (FailSave) throw new InvalidOperationException("save failed");
        return default;
    }
    protected internal override ValueTask OnRetiredAsync() { RetirementCalls++; return Retiring?.Invoke() ?? default; }
    protected internal override ValueTask OnReleasedAsync(PlayerReleaseReason reason) { ReleaseCalls++; return Releasing?.Invoke() ?? default; }
}

internal sealed class ExecutionSession : PlayerSession<ExecutionPlayer>
{
    internal readonly TaskCompletionSource<bool> Closed = PlayerExecutionHarness.Barrier();
    public ExecutionSession(IConnection connection) : base(connection) { }
    protected override ValueTask OnPlayerSessionClosedAsync(Exception? error)
    {
        Closed.TrySetResult(true);
        return default;
    }
}

internal sealed class PlayerExecutionHarness : IAsyncDisposable
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    internal readonly FakeTransport Transport = new();
    internal readonly PlayerRegistry<ExecutionPlayer> Registry;
    internal readonly RpcServer<ExecutionSession, PlayersRequest, PlayersResponse, PlayersUpdate> Server;
    internal Func<ExecutionSession, ValueTask>? AfterLogin;
    internal Func<ExecutionSession, ValueTask>? Handle;
    internal Action<ExecutionPlayer>? ConfigurePlayer;
    internal Func<ValueTask>? BeforeLoad;
    internal int Loads;

    internal PlayerExecutionHarness()
    {
        Registry = new PlayerRegistry<ExecutionPlayer>(async _ =>
        {
            Loads++;
            if (BeforeLoad != null) await BeforeLoad();
            var player = new ExecutionPlayer();
            ConfigurePlayer?.Invoke(player);
            return player;
        });
        var config = new PlayersConfig<ExecutionSession>();
        config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, req) =>
        {
            var result = await s.SignInAsync("guest:" + req.DeviceId);
            if (AfterLogin != null) await AfterLogin(s);
            return new LoginResponse { Gold = result.Player.Gold, IsReconnect = result.IsReconnect };
        });
        // Raw Rpc registrations must also acquire player ownership.
        config.Rpc.OnRequest<AddGoldRequest, AddGoldResponse>(async (s, req) =>
        {
            if (Handle != null) await Handle(s);
            s.Player!.Gold += req.Amount;
            return new AddGoldResponse { Gold = s.Player.Gold };
        });
        config.OnRequest<GetGoldRequest, GetGoldResponse>((s, _) =>
            new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = s.Player!.Gold }));
        Server = new(Transport, Registry.Wrap(config, c => new ExecutionSession(c)), config.Rpc);
    }

    internal static TaskCompletionSource<bool> Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task StartAsync() => Server.StartAsync();
    internal static async Task<PlayersResponse> SendAsync(FakeConnection connection, PlayersRequest request)
    {
        connection.ReceivePacket(Wrap(Channels.Request, request));
        await connection.SentSignal.WaitAsync(Timeout);
        Assert.That(connection.SentPackets.TryDequeue(out var packet), Is.True);
        Assert.That(packet![0], Is.EqualTo(Channels.Response));
        return PlayersResponse.Parser.ParseFrom(packet.AsSpan(1).ToArray());
    }
    internal static Task<PlayersResponse> LoginAsync(FakeConnection connection) =>
        SendAsync(connection, new PlayersRequest { RequestId = 1, Login = new LoginRequest { DeviceId = "a" } });
    public async ValueTask DisposeAsync()
    {
        await Server.StopAsync();
        await Registry.RetireAllAsync();
        Registry.Dispose();
    }
}
