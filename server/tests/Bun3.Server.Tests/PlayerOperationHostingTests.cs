using Bun3.Server.Hosting;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerOperationHostingTests
{
    private static IHost BuildHost(OperationStore store)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddPlayerServer<ExecutionSession, ExecutionPlayer, PlayersRequest, PlayersResponse, PlayersUpdate>(
            store: _ => store,
            configure: config =>
            {
                config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (session, request) =>
                {
                    var result = await session.SignInAsync(request.DeviceId);
                    return new LoginResponse { Gold = result.Player.Gold };
                });
                config.OnRequest<AddGoldRequest, AddGoldResponse>((session, request) =>
                {
                    session.Player!.Gold += request.Amount;
                    session.Player.MarkDirty();
                    return new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse { Gold = session.Player.Gold });
                });
                config.OnRequest<GetGoldRequest, GetGoldResponse>((session, request) =>
                    new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = session.Player!.Gold }));
            },
            serverOptions: options => options.Port = 0,
            playersOptions: options => { options.SaveInterval = TimeSpan.Zero; options.PlayerTickInterval = TimeSpan.FromMilliseconds(20); });
        return builder.Build();
    }

    [Test]
    public async Task Store_registration_routes_every_save_through_same_adapter()
    {
        var store = new OperationStore();
        using var host = BuildHost(store);
        await host.StartAsync();
        var registry = host.Services.GetRequiredService<PlayerRegistry<ExecutionPlayer>>();
        var port = host.Services.GetRequiredService<TcpTransportListener>().BoundPort!.Value;
        var connector = new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = port });
        var client = await RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(connector);
        await client.RequestAsync<LoginResponse>(new LoginRequest { DeviceId = "a" });
        await client.RequestAsync<AddGoldResponse>(new AddGoldRequest { Amount = -1 });
        var deadline = DateTime.UtcNow + PlayerExecutionHarness.Timeout;
        while (store.Rows["a"].Gold != 99 && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.That(store.Rows["a"].Gold, Is.EqualTo(99), "ticker saves through the registered adapter");
        client.Close();
        var session = registry.Wrap(new PlayersConfig<ExecutionSession>(), c => new ExecutionSession(c))(
            new FakeConnection(1, new FakeTransport()));
        var player = (await session.SignInAsync("a")).Player;
        player.Gold = 98; player.MarkDirty();
        await registry.HandleSessionClosedAsync(session);
        Assert.That(store.Rows["a"].Gold, Is.EqualTo(98));
        player.Gold = 97; player.MarkDirty();
        await host.StopAsync();
        Assert.That(store.Rows["a"].Gold, Is.EqualTo(97));
        Assert.That(player.SaveCalls, Is.Zero);
        Assert.That(player.RetirementCalls, Is.Zero);
    }

    [Test]
    public async Task Shutdown_stops_admission_before_drain_and_waits_for_temporary_only_operation()
    {
        var store = new OperationStore();
        var entered = PlayerExecutionHarness.Barrier();
        var release = PlayerExecutionHarness.Barrier();
        store.BeforeCommit = async () => { entered.TrySetResult(true); await release.Task; };
        using var host = BuildHost(store);
        await host.StartAsync();
        var registry = host.Services.GetRequiredService<PlayerRegistry<ExecutionPlayer>>();
        var operation = new TransferOperation();
        var running = registry.ExecuteAsync(operation).AsTask();
        await entered.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(registry.Players, Is.Empty);
        var stopping = host.StopAsync();
        Assert.That((await registry.ExecuteAsync(new TransferOperation())).Status, Is.EqualTo(RpcStatus.InvalidOperation));
        var loadedBeforeRejectedLogin = store.Loaded.Count;
        var factory = registry.Wrap(new PlayersConfig<ExecutionSession>(), c => new ExecutionSession(c));
        var newSession = factory(new FakeConnection(3, new FakeTransport()));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await newSession.SignInAsync("c"));
        Assert.That(newSession.Player, Is.Null);
        Assert.That(store.Loaded.Count, Is.EqualTo(loadedBeforeRejectedLogin), "closed admission must not load or attach a new player");
        Assert.That(stopping.IsCompleted, Is.False);
        release.TrySetResult(true);
        Assert.That((await running).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        await stopping.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(store.Rows["a"].Gold, Is.EqualTo(90));
        Assert.That(store.Rows["b"].Gold, Is.EqualTo(30));
    }

    [Test]
    public async Task Shutdown_and_old_detach_cannot_save_or_remove_replacement()
    {
        var store = new OperationStore();
        using var host = BuildHost(store);
        await host.StartAsync();
        var registry = host.Services.GetRequiredService<PlayerRegistry<ExecutionPlayer>>();
        var factory = registry.Wrap(new PlayersConfig<ExecutionSession>(), c => new ExecutionSession(c));
        var oldSession = factory(new FakeConnection(1, new FakeTransport()));
        var oldPlayer = (await oldSession.SignInAsync("a")).Player;
        var held = PlayerExecutionHarness.Barrier();
        var release = PlayerExecutionHarness.Barrier();
        store.BeforeLock = async key =>
        {
            if (key == "a") { held.TrySetResult(true); await release.Task; }
        };
        store.FailWriteAt = 1;
        var failingOperation = registry.ExecuteAsync(new TransferOperation()).AsTask();
        await held.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        var replacementSession = factory(new FakeConnection(2, new FakeTransport()));
        var replacementLogin = replacementSession.SignInAsync("a").AsTask();
        var oldDetach = registry.HandleSessionClosedAsync(oldSession).AsTask();
        release.TrySetResult(true);
        Assert.That((await failingOperation.WaitAsync(PlayerExecutionHarness.Timeout)).Outcome,
            Is.EqualTo(PlayerOperationOutcome.Failed));
        store.FailWriteAt = 0;
        var replacement = (await replacementLogin.WaitAsync(PlayerExecutionHarness.Timeout)).Player;
        Assert.That(replacement, Is.Not.SameAs(oldPlayer));
        Assert.That(replacement.Gold, Is.EqualTo(100), "replacement reloads the confirmed DB state");
        await oldDetach.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(oldPlayer.IsValid, Is.False);
        Assert.That(oldPlayer.ReleaseCalls, Is.EqualTo(1));
        replacement.Gold = 77; replacement.MarkDirty();
        Assert.That(registry.TryGet("a"), Is.SameAs(replacement));
        Assert.That(replacementSession.Player, Is.SameAs(replacement));
        Assert.That(store.Rows["a"].Gold, Is.EqualTo(100));
        await host.StopAsync();
        Assert.That(store.Rows["a"].Gold, Is.EqualTo(77));
        Assert.That(replacement.SaveCalls, Is.Zero);
        Assert.That(replacement.RetirementCalls, Is.Zero);
    }
}
