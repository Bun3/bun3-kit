using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class OwnedPlayerStoreTests
{
    [Test]
    public async Task Save_without_version_column_succeeds()
    {
        var store = OwnedStoreHarness.Online(); using var registry = OwnedStoreHarness.ReadyRegistry(store);
        var player = (await OwnedStoreHarness.Session(registry).SignInAsync("a")).Player;
        player.Gold = 115; player.MarkDirty(); await registry.SaveAsync(player);
        Assert.That(store.Rows["a"], Is.EqualTo(115)); Assert.That(player.IsDirty, Is.False);
        Assert.That(store.Writes, Is.EqualTo(1)); Assert.That(registry.TryGet("a"), Is.SameAs(player));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task Missing_or_foreign_anchor_never_loads_or_applies(bool foreign)
    {
        var store = OwnedStoreHarness.Offline();
        if (foreign) store.Foreign.Add("a"); else store.Rows.Remove("a");
        using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var op = new PlayerOperationActorTests.ActorOperation(new(PlayerOperationActorKind.Administrator, "admin"));
        Assert.That((await registry.ExecuteAsync(op)).Status, Is.EqualTo(RpcStatus.PlayerNotFound));
        Assert.That(store.Loaded, Is.Empty); Assert.That(op.ApplyCalls, Is.Zero);
    }
    [Test]
    public async Task Unknown_commit_reload_still_waits_for_anchor()
    {
        var store = OwnedStoreHarness.Online(); using var registry = OwnedStoreHarness.ReadyRegistry(store);
        var first = new PlayerOperationActorTests.ActorOperation(new(PlayerOperationActorKind.Administrator, "admin"));
        store.Control.LoseCommitAck = true;
        Assert.That((await registry.ExecuteAsync(first)).Outcome, Is.EqualTo(PlayerOperationOutcome.Unknown));
        store.Control.LoseCommitAck = false;
        var entered = PlayerExecutionHarness.Barrier(); var release = PlayerExecutionHarness.Barrier();
        store.BeforeAnchor = async _ => { entered.TrySetResult(true); await release.Task; };
        var replay = new PlayerOperationActorTests.ActorOperation(first.Actor, first.Id);
        var executing = registry.ExecuteAsync(replay).AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(executing.IsCompleted, Is.False); Assert.That(replay.ApplyCalls, Is.Zero);
        }
        finally { release.TrySetResult(true); }
        Assert.That((await executing).Outcome, Is.EqualTo(PlayerOperationOutcome.AlreadyCommitted));
        Assert.That(store.Rows["a"], Is.EqualTo(107));
    }
    [Test]
    public async Task Authority_loss_invalidates_after_releasing_current_lane()
    {
        var store = OwnedStoreHarness.Online(); using var registry = OwnedStoreHarness.ReadyRegistry(store);
        var first = (await OwnedStoreHarness.Session(registry).SignInAsync("a")).Player;
        await OwnedStoreHarness.Session(registry).SignInAsync("b");
        store.Control.Row = new(PlayerWriteState.Online, Guid.NewGuid(), Guid.NewGuid(), new byte[32]);
        using (PlayerExecutionLane.Install())
        {
            var lane = PlayerExecutionLane.Current!;
            lane.TakeOwnership(await registry.AcquireAsync("a"));
            try { first.Gold = 0; first.MarkDirty(); await registry.SaveAsync(first); }
            finally { lane.ReleaseOwnership(); }
        }
        await registry.AuthorityCleanup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(registry.Players, Is.Empty); Assert.That(store.Writes, Is.Zero);
        await registry.SaveAsync(first); Assert.That(store.Writes, Is.Zero);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await OwnedStoreHarness.Session(registry).SignInAsync("a"));
    }
    [Test]
    public async Task Existing_versioned_adapter_detects_stale_write()
    {
        var store = new FaultingPlayerStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var player = (await OwnedStoreHarness.Session(registry).SignInAsync("a")).Player;
        store.Version++; store.Gold = 125; player.Gold = 0; player.MarkDirty();
        await registry.SaveAsync(player);
        Assert.That(store.Writes, Is.Zero); Assert.That(store.Gold, Is.EqualTo(125)); Assert.That(registry.TryGet("a"), Is.Null);
    }
    [Test]
    public void Offline_executor_rejects_session_and_ticker_registration()
    {
        using var registry = new PlayerRegistry<ExecutionPlayer>(OwnedStoreHarness.Offline());
        Assert.Throws<InvalidOperationException>(() => OwnedStoreHarness.Session(registry));
        Assert.Throws<InvalidOperationException>(() => new PlayerTicker<ExecutionPlayer>(registry));
    }
    [Test]
    public async Task Offline_executor_always_loads_and_discards_fresh_state()
    {
        var store = OwnedStoreHarness.Offline(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        for (var i = 0; i < 2; i++)
            Assert.That((await registry.ExecuteAsync(new PlayerOperationActorTests.ActorOperation(new(PlayerOperationActorKind.Administrator, "admin")))).Outcome,
                Is.EqualTo(PlayerOperationOutcome.Committed));
        Assert.That(store.Rows["a"], Is.EqualTo(114)); Assert.That(registry.Players, Is.Empty);
        Assert.That(store.Loaded, Has.Count.EqualTo(2)); Assert.That(store.Loaded.All(p => !p.IsValid && p.ReleaseCalls == 1), Is.True);
    }
}
