using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Tests.PlayersProtocol;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerWriteLifecycleTests
{
    private static (OwnedStoreHarness Store, PlayerRegistry<ExecutionPlayer> Registry, PlayerServerLifecycle<ExecutionPlayer> Life) NewServer()
    {
        var control = new AuthorityStoreHarness(); var store = new OwnedStoreHarness(control, new(PlayerWriteRole.Server, Guid.NewGuid()));
        var registry = new PlayerRegistry<ExecutionPlayer>(store); return (store, registry, new(registry));
    }
    private static async Task Start(PlayerServerLifecycle<ExecutionPlayer> life)
    {
        Assert.That((await life.PrepareAsync()).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Committed));
        Assert.That((await life.ActivateAsync()).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Committed));
    }
    [Test]
    public async Task Server_registry_starts_with_admission_closed()
    {
        var (store, registry, life) = NewServer(); using var cleanup = registry;
        Assert.ThrowsAsync<InvalidOperationException>(async () => await OwnedStoreHarness.Session(registry).SignInAsync("a"));
        await life.PrepareAsync();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await OwnedStoreHarness.Session(registry).SignInAsync("a"));
        Assert.That(store.Loaded, Is.Empty);
        await life.ActivateAsync();
        Assert.That((await OwnedStoreHarness.Session(registry).SignInAsync("a")).Player.Gold, Is.EqualTo(100));
    }
    [Test]
    public async Task Maintenance_flag_alone_does_not_allow_offline_write()
    {
        var (store, registry, life) = NewServer(); using var cleanup = registry; await Start(life);
        registry.StopAcceptingOperations();
        var offline = new OwnedStoreHarness(store.Control, new(PlayerWriteRole.OfflineAdministrator, Guid.Empty));
        using var admin = new PlayerRegistry<ExecutionPlayer>(offline);
        var result = await admin.ExecuteAsync(new PlayerOperationActorTests.ActorOperation(new(PlayerOperationActorKind.Administrator, "admin")));
        Assert.That(result.Outcome, Is.Not.EqualTo(PlayerOperationOutcome.Committed));
        Assert.That(offline.Writes, Is.Zero); Assert.That(store.Control.Row!.State, Is.EqualTo(PlayerWriteState.Online));
    }
    [TestCase("handler")]
    [TestCase("posted")]
    [TestCase("offline")]
    public async Task Drain_waits_for_handler_posted_work_and_offline_operation(string kind)
    {
        if (kind == "offline")
        {
            using var commandHost = new CommandHarness(); var command = CommandHarness.Envelope(); await commandHost.Queue.SubmitAsync(command);
            var saving = PlayerExecutionHarness.Barrier(); var resume = PlayerExecutionHarness.Barrier();
            commandHost.Commands.BeforeComplete = async _ => { saving.TrySetResult(true); await resume.Task; };
            var operation = commandHost.Dispatcher.ExecuteAsync(command.Id).AsTask();
            await saving.Task.WaitAsync(TimeSpan.FromSeconds(5)); var draining = commandHost.Registry.DrainAsync().AsTask();
            try { Assert.That(draining.IsCompleted, Is.False); } finally { resume.TrySetResult(true); }
            Assert.That((await operation)!.Status, Is.EqualTo(PlayerCommandStatus.Committed));
            Assert.That((await draining).IsQuiescent, Is.True); return;
        }
        await using var h = new PlayerExecutionHarness(); await h.StartAsync(); var connection = h.Transport.Connect(1);
        await PlayerExecutionHarness.LoginAsync(connection);
        var entered = PlayerExecutionHarness.Barrier(); var release = PlayerExecutionHarness.Barrier();
        h.Handle = async _ => { entered.TrySetResult(true); await release.Task; };
        if (kind == "handler") connection.ReceivePacket(PacketTestHelper.Wrap(Channels.Request, new PlayersRequest { AddGold = new AddGoldRequest { Amount = 1 } }));
        else h.Registry.TryGet("guest:a")!.CurrentSession!.Post(async () => { entered.TrySetResult(true); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var drain = h.Registry.DrainAsync().AsTask();
        try { Assert.That(drain.IsCompleted, Is.False); } finally { release.TrySetResult(true); }
        Assert.That((await drain).IsQuiescent, Is.True);
    }
    [Test]
    public async Task New_request_after_gate_close_does_not_enter_handler()
    {
        await using var h = new PlayerExecutionHarness(); await h.StartAsync(); var connection = h.Transport.Connect(1);
        await PlayerExecutionHarness.LoginAsync(connection); var calls = 0;
        h.Handle = _ => { calls++; return default; }; h.Registry.StopAcceptingOperations();
        var result = await PlayerExecutionHarness.SendAsync(connection, new PlayersRequest { AddGold = new AddGoldRequest { Amount = 1 } });
        Assert.That(result.Status, Is.EqualTo(RpcStatus.InvalidOperation)); Assert.That(calls, Is.Zero);
    }
    [Test]
    public async Task Detached_save_cannot_start_after_final_drain()
    {
        var (store, registry, life) = NewServer(); using var cleanup = registry; await Start(life);
        var session = OwnedStoreHarness.Session(registry); var player = (await session.SignInAsync("a")).Player;
        player.Gold = 110; player.MarkDirty(); await life.BeginDrainAsync(); var result = await life.CompleteDrainAsync();
        await registry.HandleSessionClosedAsync(session);
        Assert.That(result.OfflineConfirmed, Is.True); Assert.That(store.Writes, Is.EqualTo(1)); Assert.That(registry.Players, Is.Empty);
    }
    [Test]
    public async Task Unknown_save_prevents_offline_publication()
    {
        var (store, registry, life) = NewServer(); using var cleanup = registry; await Start(life);
        var player = (await OwnedStoreHarness.Session(registry).SignInAsync("a")).Player; player.MarkDirty();
        store.Control.LoseCommitAck = true; await registry.SaveAsync(player); store.Control.LoseCommitAck = false;
        await life.BeginDrainAsync();
        store.BeforeAnchor = _ => throw new IOException("previous writer cannot be confirmed");
        var result = await life.CompleteDrainAsync();
        Assert.That(result.OfflineConfirmed, Is.False); Assert.That(result.Drain.UnresolvedKeys, Does.Contain("a"));
        Assert.That(store.Control.Row!.State, Is.Not.EqualTo(PlayerWriteState.Offline));
    }
    [Test]
    public async Task Release_hook_failure_prevents_offline_publication()
    {
        var (store, registry, life) = NewServer(); using var cleanup = registry; await Start(life);
        var player = (await OwnedStoreHarness.Session(registry).SignInAsync("a")).Player;
        player.Releasing = () => throw new InvalidOperationException("cleanup failed"); await life.BeginDrainAsync();
        var result = await life.CompleteDrainAsync();
        Assert.That(result.Drain.IsQuiescent, Is.False); Assert.That(result.Drain.Errors, Is.Not.Empty);
        Assert.That(result.OfflineConfirmed, Is.False); Assert.That(store.Control.Row!.State, Is.Not.EqualTo(PlayerWriteState.Offline));
    }
    [Test]
    public async Task Offline_ack_loss_does_not_reopen_old_registry()
    {
        var (store, registry, life) = NewServer(); using var cleanup = registry; await Start(life); await life.BeginDrainAsync();
        store.Control.LoseCommitAck = true; var unknown = await life.CompleteDrainAsync();
        Assert.That(unknown.OfflineConfirmed, Is.False); Assert.That(unknown.Transition!.Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Unknown));
        Assert.That(store.Control.Row!.State, Is.EqualTo(PlayerWriteState.Offline));
        store.Control.LoseCommitAck = false; Assert.That((await life.CompleteDrainAsync()).OfflineConfirmed, Is.True);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await OwnedStoreHarness.Session(registry).SignInAsync("a"));
    }
    [Test]
    public async Task Second_server_cannot_activate()
    {
        var (store, registry, life) = NewServer(); using var cleanup = registry; await Start(life);
        using var other = new PlayerRegistry<ExecutionPlayer>(new OwnedStoreHarness(store.Control, new(PlayerWriteRole.Server, Guid.NewGuid())));
        var second = new PlayerServerLifecycle<ExecutionPlayer>(other);
        Assert.That((await second.PrepareAsync()).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Conflict));
        Assert.That((await second.ActivateAsync()).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Conflict));
    }
    [Test]
    public async Task Restart_loads_admin_committed_state()
    {
        var (store, registry, life) = NewServer(); using var cleanup = registry; await Start(life);
        var player = (await OwnedStoreHarness.Session(registry).SignInAsync("a")).Player;
        player.Gold = 115; player.MarkDirty(); await life.BeginDrainAsync();
        Assert.That((await life.CompleteDrainAsync()).OfflineConfirmed, Is.True);
        var offlineStore = new OwnedStoreHarness(store.Control, new(PlayerWriteRole.OfflineAdministrator, Guid.Empty), store.Rows, store.Receipts);
        using (var offline = new PlayerRegistry<ExecutionPlayer>(offlineStore))
            Assert.That((await offline.ExecuteAsync(new PlayerOperationActorTests.ActorOperation(new(PlayerOperationActorKind.Administrator, "admin")))).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        var nextStore = new OwnedStoreHarness(store.Control, new(PlayerWriteRole.Server, Guid.NewGuid()), store.Rows, store.Receipts);
        using var next = new PlayerRegistry<ExecutionPlayer>(nextStore); await Start(new(next));
        Assert.That((await OwnedStoreHarness.Session(next).SignInAsync("a")).Player.Gold, Is.EqualTo(122));
    }
    [Test]
    public async Task Manual_recovery_checks_old_instance_and_database_barrier()
    {
        var (store, registry, life) = NewServer(); using var cleanup = registry; await Start(life);
        var wrong = new PlayerWriteRecoveryConfirmation(Guid.NewGuid(), Guid.NewGuid(), "operator", "process stopped");
        Assert.That((await store.Authority.RecoverAsync(wrong)).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Conflict));
        var confirmed = new PlayerWriteRecoveryConfirmation(Guid.NewGuid(), store.Binding.InstanceId, "operator", "process stopped and restart disabled");
        store.Control.FailRecovery = true;
        Assert.ThrowsAsync<InvalidOperationException>(async () => await store.Authority.RecoverAsync(confirmed));
        Assert.That(store.Control.Row!.State, Is.EqualTo(PlayerWriteState.Online));
        store.Control.FailRecovery = false;
        Assert.That((await store.Authority.RecoverAsync(confirmed)).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Committed));
        var audits = store.Control.Audits;
        Assert.That((await store.Authority.RecoverAsync(confirmed)).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Committed));
        Assert.That(store.Control.Audits, Is.EqualTo(audits));
        var changed = new PlayerWriteRecoveryConfirmation(confirmed.Id, confirmed.ExpectedInstanceId, "operator", "different reason");
        Assert.That((await store.Authority.RecoverAsync(changed)).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Conflict));
    }
}
