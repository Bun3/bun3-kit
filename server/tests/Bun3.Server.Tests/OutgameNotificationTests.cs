using Bun3.Gameplay.Numerics;
using Bun3.Server.Achievements;
using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class OutgameNotificationTests
{
    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold, _reward, _sword;
    private long _next;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>().Register("gold", "Gold")
            .Register("reward", "Reward").Register("sword", "Sword", unstackable: true).Build();
        _gold = _catalog.GetRequired("gold");
        _reward = _catalog.GetRequired("reward");
        _sword = _catalog.GetRequired("sword");
        _next = 0;
    }

    private ItemInventory<int> Inventory(Transaction? tx = null, Action? dirty = null,
        InventoryAppliedHandler? applied = null, ActionLog? log = null) =>
        new(_catalog, () => ++_next, _ => 0, onDirty: dirty, onApplied: applied, log: log, transaction: tx);

    [Test]
    public void Dirty_failure_finalizes_later_inventory_and_preserves_next_rollback()
    {
        var tx = new Transaction();
        var failure = new InvalidOperationException("dirty");
        var dirty = 0;
        var firstApplied = 0;
        var first = Inventory(tx, () => throw failure, _ => firstApplied++);
        var second = Inventory(tx, () => dirty++);
        using (var scope = tx.Begin())
        {
            first.TryAdd(_gold, 1);
            second.TryAdd(_gold, 5);
            Assert.That(Assert.Catch(scope.Commit), Is.SameAs(failure));
        }
        using (tx.Begin()) second.TryRemove(_gold, 2);
        Assert.That(second.GetQuantity(_gold), Is.EqualTo((BigNum)5));
        Assert.That(firstApplied, Is.EqualTo(1));
        second.TryAdd(_gold, 1);
        Assert.That(dirty, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Reentry_preserves_current_span_and_causal_ledger(bool commit)
    {
        var tx = new Transaction();
        var entries = new List<ActionLogEntry>();
        var log = new ActionLog(batch => { foreach (var entry in batch) entries.Add(entry); });
        ItemInventory<int> inv = null!;
        inv = Inventory(tx, applied: batch =>
        {
            if (batch[0].Item != _gold) return;
            inv.TryAdd(_reward, 3);
            Assert.That(batch.Length, Is.EqualTo(1));
            Assert.That(batch[0].Item, Is.EqualTo(_gold));
            Assert.That(batch[0].Delta, Is.EqualTo((BigNum)2));
        }, log: log);
        if (commit)
        {
            using var scope = tx.Begin();
            inv.TryAdd(_gold, 2);
            scope.Commit();
        }
        else inv.TryAdd(_gold, 2);
        Assert.That(entries.Count, Is.EqualTo(2));
        Assert.That(((InventoryChange)entries[0].Data!).Item, Is.EqualTo(_gold));
        Assert.That(((InventoryChange)entries[0].Data!).Delta, Is.EqualTo((BigNum)2));
        Assert.That(((InventoryChange)entries[1].Data!).Item, Is.EqualTo(_reward));
        Assert.That(((InventoryChange)entries[1].Data!).Delta, Is.EqualTo((BigNum)3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Earlier_inventory_reaction_keeps_later_commit_batch_frozen(bool failReaction)
    {
        var tx = new Transaction();
        var deltas = new List<BigNum>();
        var later = Inventory(tx, applied: batch => { foreach (var row in batch) deltas.Add(row.Delta); });
        var first = Inventory(tx, applied: _ =>
        {
            later.TryAdd(_gold, 3);
            if (failReaction) throw new InvalidOperationException("reaction");
        });
        using (var scope = tx.Begin())
        {
            first.TryAdd(_gold, 1);
            later.TryAdd(_gold, 5);
            if (failReaction) Assert.Throws<InvalidOperationException>(scope.Commit);
            else scope.Commit();
        }
        Assert.That(deltas, Is.EqualTo(new BigNum[] { 3, 5 }));
        using (tx.Begin()) later.TryRemove(_gold, 2);
        Assert.That(later.GetQuantity(_gold), Is.EqualTo((BigNum)8));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Transfer_cleans_both_batches_even_when_source_dirty_throws(bool fail)
    {
        var tx = new Transaction();
        var armed = false;
        var deltas = new List<BigNum>();
        var entries = new List<ActionLogEntry>();
        var log = new ActionLog(batch => { foreach (var row in batch) entries.Add(row); });
        var source = Inventory(tx, () => { if (armed && fail) throw new InvalidOperationException("transfer"); });
        var target = Inventory(tx, applied: batch => { foreach (var row in batch) deltas.Add(row.Delta); }, log: log);
        var created = new List<ItemInstance<int>>();
        source.TryAdd(_sword, 1, created);
        armed = true;
        if (fail) Assert.Throws<InvalidOperationException>(() => source.TryTransfer(target, _sword, 1));
        else source.TryTransfer(target, _sword, 1);
        using (var scope = tx.Begin()) { created[0].Flags = 4; scope.Commit(); }
        Assert.That(deltas, Is.EqualTo(new BigNum[] { 1 }));
        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(((InventoryChange)entries[0].Data!).Delta, Is.EqualTo((BigNum)1));
        using (tx.Begin()) target.TryRemove(_sword, 1);
        Assert.That(target.GetQuantity(_sword), Is.EqualTo((BigNum)1));
        using (tx.Begin()) source.TryAdd(_gold, 2);
        Assert.That(source.GetQuantity(_gold), Is.EqualTo(BigNum.Zero));
    }

    private readonly record struct State(int Number, List<int> Values);

    [TestCase(false)]
    [TestCase(true)]
    public void Struct_replacement_then_mutation_restores_original_value(bool shared)
    {
        var tx = new Transaction();
        var original = new State(1, new List<int> { 10 });
        var inv = new ItemInventory<State>(_catalog, () => ++_next, _ => original,
            transaction: tx, snapshotState: state => new State(state.Number, new List<int>(state.Values)));
        var created = new List<ItemInstance<State>>();
        inv.TryAdd(_sword, 1, created);
        var item = created[0];
        using (tx.Begin())
        {
            item.State = new State(5, shared ? original.Values : new List<int> { 20 });
            using (item.BeginStateChange()) item.State.Values.Add(30);
        }
        Assert.That(item.State.Number, Is.EqualTo(1));
        Assert.That(item.State.Values, Is.EqualTo(new[] { 10 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Achievement_channels_survive_log_and_dirty_failure_without_replay(bool commit)
    {
        var tx = new Transaction();
        var catalog = new AchievementCatalog<AchievementDefinition>(new[] { new AchievementDefinition("first", 1) });
        var id = catalog.GetRequired("first");
        var fail = true;
        var dirty = 0;
        var completed = 0;
        var logged = 0;
        var manager = new AchievementManager<AchievementDefinition>(catalog,
            onDirty: () => { dirty++; if (fail) throw new ArgumentException("dirty"); },
            log: new ActionLog(_ => { logged++; if (fail) throw new InvalidOperationException("log"); }), transaction: tx);
        manager.OnCompleted += (_, _, _) => completed++;
        if (commit)
        {
            using var scope = tx.Begin();
            manager.Increase(id, 1);
            var error = Assert.Catch<AggregateException>(scope.Commit);
            Assert.That(error!.Flatten().InnerExceptions.Count, Is.EqualTo(2));
        }
        else Assert.Throws<AggregateException>(() => manager.Increase(id, 1));
        Assert.That(completed, Is.EqualTo(1));
        Assert.That(dirty, Is.EqualTo(1));
        fail = false;
        using (tx.Begin()) manager.TryClaim(id);
        Assert.That(manager.GetClaimableCount(id), Is.EqualTo(1));
        using (var scope = tx.Begin()) { manager.TryClaim(id); scope.Commit(); }
        Assert.That(logged, Is.EqualTo(2));
        Assert.That(completed, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Inventory_log_dirty_and_applied_failures_are_all_attempted_without_replay(bool commit)
    {
        var tx = new Transaction();
        var fail = true;
        var dirty = 0;
        var applied = 0;
        var logged = 0;
        var inv = Inventory(tx, () => { dirty++; if (fail) throw new ArgumentException("dirty"); },
            _ => { applied++; if (fail) throw new NotSupportedException("applied"); },
            new ActionLog(_ => { logged++; if (fail) throw new InvalidOperationException("log"); }));
        if (commit)
        {
            using var scope = tx.Begin();
            inv.TryAdd(_gold, 2);
            var error = Assert.Catch<AggregateException>(scope.Commit);
            Assert.That(error!.Flatten().InnerExceptions.Count, Is.EqualTo(3));
        }
        else
        {
            var error = Assert.Catch<AggregateException>(() => inv.TryAdd(_gold, 2));
            Assert.That(error!.Flatten().InnerExceptions.Count, Is.EqualTo(3));
        }
        fail = false;
        using (tx.Begin()) inv.TryRemove(_gold, 1);
        using (var scope = tx.Begin()) { inv.TryAdd(_gold, 1); scope.Commit(); }
        Assert.That(inv.GetQuantity(_gold), Is.EqualTo((BigNum)3));
        Assert.That(new[] { logged, dirty, applied }, Is.EqualTo(new[] { 2, 2, 2 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Inventory_reaction_to_later_achievement_keeps_committed_completion_and_change(bool fail)
    {
        var tx = new Transaction();
        var catalog = new AchievementCatalog<AchievementDefinition>(new[] { new AchievementDefinition("first", 1) });
        var id = catalog.GetRequired("first");
        var kinds = new List<AchievementChangeKind>();
        var completions = new List<int>();
        var manager = new AchievementManager<AchievementDefinition>(catalog, transaction: tx,
            log: new ActionLog(batch => { foreach (var row in batch) kinds.Add(((AchievementChange)row.Data!).Kind); }));
        manager.OnCompleted = (_, _, count) =>
        {
            completions.Add(count);
            if (fail) throw new InvalidOperationException("completed");
        };
        var inv = Inventory(tx, applied: _ => manager.TryClaim(id));
        using (var scope = tx.Begin())
        {
            inv.TryAdd(_gold, 2);
            manager.Increase(id, 1);
            if (fail) Assert.Throws<InvalidOperationException>(scope.Commit);
            else scope.Commit();
        }
        Assert.That(kinds, Is.EqualTo(new[] { AchievementChangeKind.Claimed, AchievementChangeKind.Completed }));
        Assert.That(completions, Is.EqualTo(new[] { 1 }));
        using (tx.Begin()) manager.Reset(id);
        Assert.That(manager.GetState(id).CompletedCount, Is.EqualTo(1));
        Assert.That(manager.GetState(id).ClaimedCount, Is.EqualTo(1));
    }

    [Test]
    public void Failing_completion_does_not_skip_later_completions_or_inventory_participant()
    {
        var tx = new Transaction();
        var catalog = new AchievementCatalog<AchievementDefinition>(new[]
        {
            new AchievementDefinition("first", 1), new AchievementDefinition("second", 1),
        });
        var first = catalog.GetRequired("first");
        var second = catalog.GetRequired("second");
        var completions = new List<AchievementId>();
        var manager = new AchievementManager<AchievementDefinition>(catalog, transaction: tx);
        manager.OnCompleted = (id, _, _) =>
        {
            completions.Add(id);
            if (id == first) throw new InvalidOperationException("first");
        };
        var applied = 0;
        var inv = Inventory(tx, applied: _ => applied++);
        using (var scope = tx.Begin())
        {
            manager.Increase(first, 1);
            manager.Increase(second, 1);
            inv.TryAdd(_gold, 5);
            Assert.Throws<InvalidOperationException>(scope.Commit);
        }
        Assert.That(completions, Is.EqualTo(new[] { first, second }));
        Assert.That(applied, Is.EqualTo(1));
        using (tx.Begin()) inv.TryRemove(_gold, 2);
        Assert.That(inv.GetQuantity(_gold), Is.EqualTo((BigNum)5));
        using (var scope = tx.Begin()) { manager.TryClaim(second); scope.Commit(); }
        Assert.That(completions.Count, Is.EqualTo(2));
    }

    [Test]
    public void Reentrant_builder_survives_outer_builder_notification_cleanup()
    {
        ItemInventory<int> inv = null!;
        InventoryTransaction<int> next = default;
        inv = Inventory(applied: batch =>
        {
            if (batch[0].Item != _gold) return;
            next = inv.BeginTransaction();
            next.Add(_reward, 3);
        });
        var first = inv.BeginTransaction();
        first.Add(_gold, 2);
        Assert.That(first.Commit(out _), Is.EqualTo(InventoryError.None));
        Assert.That(next.Commit(out _), Is.EqualTo(InventoryError.None));
        Assert.That(inv.GetQuantity(_reward), Is.EqualTo((BigNum)3));
    }

    [Test]
    public void Action_log_sink_reentry_preserves_live_span_even_when_nested_sink_throws()
    {
        ActionLog log = null!;
        log = new ActionLog(batch =>
        {
            if (batch[0].Text != "outer") throw new InvalidOperationException("inner");
            Assert.Throws<InvalidOperationException>(() => log.Log("inner"));
            Assert.That(batch[0].Text, Is.EqualTo("outer"));
        });
        log.Log("outer");
        log.Log("outer");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Regen_advances_basis_before_callback_failure_or_recursive_settlement(bool fail)
    {
        var catalog = new ItemCatalogBuilder<string>().Register("energy", "Energy", maxCount: 100,
            regenPeriodTicks: 10, maxRegen: 100).Build();
        var energy = catalog.GetRequired("energy");
        ItemInventory<int> inv = null!;
        var calls = 0;
        inv = new ItemInventory<int>(catalog, () => ++_next, _ => 0, onApplied: _ =>
        {
            calls++;
            if (calls != 1) return;
            if (fail) throw new InvalidOperationException("regen");
            Assert.That(inv.SettleRegen(30), Is.EqualTo(0));
        });
        inv.LoadRegenBasis(energy, 10);
        if (fail) Assert.Throws<InvalidOperationException>(() => inv.SettleRegen(30));
        else Assert.That(inv.SettleRegen(30), Is.EqualTo(1));
        Assert.That(inv.GetRegenBasis(energy), Is.EqualTo(30));
        Assert.That(inv.GetQuantity(energy), Is.EqualTo((BigNum)2));
        Assert.That(inv.SettleRegen(30), Is.EqualTo(0));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void Warmed_reentrant_applied_path_is_allocation_free()
    {
        ItemInventory<int> inv = null!;
        var stable = true;
        inv = Inventory(applied: batch =>
        {
            if (batch[0].Item != _gold) return;
            inv.TryAdd(_reward, 3);
            stable &= batch[0].Item == _gold && batch[0].Delta == (BigNum)2;
        });
        for (var i = 0; i < 100; i++) inv.TryAdd(_gold, 2);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) inv.TryAdd(_gold, 2);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(stable, Is.True);
        Assert.That(allocated, Is.Zero);
        Assert.That(inv.GetQuantity(_reward), Is.EqualTo((BigNum)3300));
    }
}
