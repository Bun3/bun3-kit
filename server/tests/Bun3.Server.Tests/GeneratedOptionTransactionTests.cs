using Bun3.Server.Core;
using Bun3.Server.Achievements;
using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using Bun3.State;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class GeneratedOptionTransactionTests
{
    private sealed class ThrowingRestoreTarget : IStateValueTarget<int>
    {
        internal readonly InvalidOperationException Failure = new("custom restore failed");
        internal Action? BeforeThrow;
        public void RestoreValue(int slot, int value)
        {
            BeforeThrow?.Invoke();
            throw Failure;
        }
    }

    private sealed class ThrowingDetachState : ITrackedState
    {
        private readonly StateNode _tracking;
        internal readonly InvalidOperationException Failure = new("custom ownership cleanup failed");
        internal bool FailTracking;
        internal ThrowingDetachState() => _tracking = new StateNode(this);
        StateNode ITrackedNode.Tracking => FailTracking ? throw Failure : _tracking;
        void ITrackedNode.VisitChildren(IStateChildVisitor visitor) { }
    }

    [Test]
    public void Journal_and_independent_cleanup_failures_are_preserved_while_other_items_finalize()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var sword = catalog.GetRequired("sword");
        var tx = new Transaction();
        var brokenRoot = new ThrowingDetachState();
        var inventory = new ItemInventory<object>(catalog, () => 2, _ => brokenRoot, transaction: tx);
        var root = new GeneratedOptionState();
        inventory.TryLoadInstance(1, sword, 1, 0, root);
        inventory.TryGetInstance(1, out var item);
        var target = new ThrowingRestoreTarget { BeforeThrow = () => brokenRoot.FailTracking = true };
        var scope = tx.Begin();
        root.Level = 5;
        item.Flags = 2;
        inventory.TryAdd(sword, 1);
        inventory.TryGetInstance(2, out var phantom);
        ((IStateOwner)item).BeforeChange()!.RecordValue(target, 0, 0);

        var error = Assert.Throws<AggregateException>(() => scope.Dispose())!;
        Assert.That(error.InnerExceptions, Is.EqualTo(new[] { target.Failure, brokenRoot.Failure }));
        Assert.That(inventory.InstanceCount, Is.EqualTo(1));
        Assert.That(item.Flags, Is.Zero);
        Assert.That(root.Level, Is.EqualTo(1));
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(item.StateTransactionOwner, Is.Null);
        Assert.That(phantom.StateTransactionOwner, Is.Null);
        Assert.That(item.TxToken, Is.Zero);
        Assert.That(phantom.TxToken, Is.Zero);
        Assert.That(item.TxSnapshotIndex, Is.EqualTo(-1));
        Assert.That(phantom.TxSnapshotIndex, Is.EqualTo(-1));
        using (tx.Begin()) { item.Flags = 3; }
        Assert.That(item.Flags, Is.Zero);
        using (tx.Begin()) { root.Level = 9; }
        Assert.That(root.Level, Is.EqualTo(1));
    }

    [Test]
    public void Journal_failure_still_restores_inventory_cleans_ownership_and_allows_transaction_reuse()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("material", "Material")
            .Register("sword", "Sword", unstackable: true).Build();
        var material = catalog.GetRequired("material");
        var sword = catalog.GetRequired("sword");
        var tx = new Transaction();
        var dirty = 0;
        var applied = 0;
        long next = 2;
        var inventory = new ItemInventory<GeneratedOptionState>(catalog, () => ++next, _ => new(),
            transaction: tx, onDirty: () => dirty++, onApplied: _ => applied++);
        var root = new GeneratedOptionState();
        inventory.TryLoadInstance(1, material, 10, 0, new());
        inventory.TryLoadInstance(2, sword, 1, 0, root);
        inventory.TryGetInstance(2, out var item);
        item.Flags = 1;
        var achievementsCatalog = new AchievementCatalog<AchievementDefinition>(new[] { new AchievementDefinition("upgrade", 1) });
        var achievement = achievementsCatalog.GetRequired("upgrade");
        var achievements = new AchievementManager<AchievementDefinition>(achievementsCatalog, transaction: tx);
        var target = new ThrowingRestoreTarget();
        var scope = tx.Begin();
        achievements.Increase(achievement, 1);
        root.Level = 5;
        var failedJournal = ((IStateOwner)item).BeforeChange()!;
        failedJournal.RecordValue(target, 0, 0);
        item.Flags = 2;
        inventory.TryRemove(material, 3);
        inventory.TryRemoveByInstance(2, 1);
        root.Option.Level = 9;
        inventory.TryAdd(sword, 1);
        inventory.TryGetInstance(3, out var phantom);
        var phantomRoot = phantom.State;
        phantomRoot.Level = 8;
        inventory.TryRemoveByInstance(3, 1);

        Assert.That(Assert.Throws<InvalidOperationException>(() => scope.Dispose()), Is.SameAs(target.Failure));
        Assert.That(tx.IsActive, Is.False);
        Assert.That(achievements.GetState(achievement).Progress, Is.Zero);
        Assert.That(inventory.InstanceCount, Is.EqualTo(2));
        Assert.That(inventory.GetQuantity(material), Is.EqualTo((BigNum)10));
        Assert.That(inventory.TryGetInstance(2, out var restored), Is.True);
        Assert.That(restored, Is.SameAs(item));
        Assert.That(item.State, Is.SameAs(root));
        Assert.That(root.Level, Is.EqualTo(1));
        Assert.That(root.Option.Level, Is.EqualTo(2));
        Assert.That(item.Flags, Is.EqualTo(1));
        Assert.That(phantomRoot.Level, Is.EqualTo(1));
        Assert.That(item.StateTransactionOwner, Is.Null);
        Assert.That(phantom.StateTransactionOwner, Is.Null);
        Assert.That(item.TxToken, Is.Zero);
        Assert.That(phantom.TxToken, Is.Zero);
        Assert.That(item.TxSnapshotIndex, Is.EqualTo(-1));
        Assert.That(phantom.TxSnapshotIndex, Is.EqualTo(-1));
        Assert.That(inventory.HasChanges, Is.True);
        Assert.That(dirty, Is.EqualTo(1));
        Assert.That(applied, Is.Zero);
        var changes = new List<ItemChange<GeneratedOptionState>>();
        inventory.DrainChanges(changes);
        Assert.That(changes.Select(change => (change.InstanceId, change.Kind)),
            Is.EqualTo(new[] { (2L, ItemChangeKind.Updated) }));
        var receiver = new ItemInventory<GeneratedOptionState>(catalog, () => 10, _ => new());
        Assert.That(receiver.TryLoadInstance(9, sword, 1, 0, phantomRoot), Is.EqualTo(InventoryError.None));
        phantomRoot.Level = 30;
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(() => failedJournal.Rollback(), Throws.InvalidOperationException,
            "Retiring the inventory reference must not reset a standalone failed journal.");

        using (tx.Begin()) { item.Flags = 4; inventory.TryRemove(material, 2); }
        Assert.That(item.Flags, Is.EqualTo(1));
        Assert.That(inventory.GetQuantity(material), Is.EqualTo((BigNum)10));
        using (var commit = tx.Begin()) { item.Flags = 6; commit.Commit(); }
        Assert.That(item.Flags, Is.EqualTo(6));
        Assert.That(dirty, Is.EqualTo(2));
        Assert.That(applied, Is.Zero);
        using (tx.Begin()) { root.Level = 7; }
        Assert.That(root.Level, Is.EqualTo(1));
        using (var commit = tx.Begin()) { root.Level = 9; commit.Commit(); }
        Assert.That(root.Level, Is.EqualTo(9));
        Assert.That(dirty, Is.EqualTo(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Commit_finishes_all_participants_before_callbacks_even_when_first_throws(bool throws)
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var sword = catalog.GetRequired("sword");
        var tx = new Transaction();
        var firstRoot = new GeneratedOptionState();
        var removedRoot = new GeneratedOptionState();
        var secondCallbacks = 0;
        var achievementCallbacks = 0;
        var achievementsCatalog = new AchievementCatalog<AchievementDefinition>(new[] { new AchievementDefinition("upgrade", 1) });
        var achievement = achievementsCatalog.GetRequired("upgrade");
        var achievements = new AchievementManager<AchievementDefinition>(achievementsCatalog, transaction: tx,
            onDirty: () => achievementCallbacks++);
        var second = new ItemInventory<GeneratedOptionState>(catalog, () => 3, _ => new(), transaction: tx,
            onDirty: () => secondCallbacks++);
        var receiver = new ItemInventory<GeneratedOptionState>(catalog, () => 9, _ => new());
        var firstCallbacks = 0;
        var first = new ItemInventory<GeneratedOptionState>(catalog, () => 2, _ => new(), transaction: tx,
            onDirty: () =>
            {
                firstCallbacks++;
                Assert.That(tx.IsActive, Is.False);
                Assert.That(second.InstanceCount, Is.Zero);
                Assert.That(achievements.GetState(achievement).Progress, Is.EqualTo(1));
                Assert.That(receiver.TryLoadInstance(8, sword, 1, 0, removedRoot), Is.EqualTo(InventoryError.None));
                if (throws) throw new InvalidOperationException("callback");
            });
        first.TryLoadInstance(1, sword, 1, 0, firstRoot);
        second.TryLoadInstance(4, sword, 1, 0, removedRoot);
        var scope = tx.Begin();
        firstRoot.Level = 5;
        removedRoot.Level = 7;
        second.TryRemoveByInstance(4, 1);
        achievements.Increase(achievement, 1);
        if (throws) Assert.That(() => scope.Commit(), Throws.InvalidOperationException);
        else scope.Commit();
        scope.Dispose();
        Assert.That(firstCallbacks, Is.EqualTo(1));
        Assert.That(secondCallbacks, Is.EqualTo(1));
        Assert.That(achievementCallbacks, Is.EqualTo(1));
        using (tx.Begin()) { firstRoot.Level = 20; }
        Assert.That(firstRoot.Level, Is.EqualTo(5));
    }

    [Test]
    public void Commit_callback_can_start_new_generated_change_and_next_rollback_reuses_journal()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var tx = new Transaction();
        var root = new GeneratedOptionState();
        var callbacks = 0;
        var inventory = new ItemInventory<GeneratedOptionState>(catalog, () => 2, _ => new(), transaction: tx,
            onDirty: () =>
            {
                callbacks++;
                if (callbacks == 1)
                {
                    root.Option.Level = 11;
                }
            });
        inventory.TryLoadInstance(1, catalog.GetRequired("sword"), 1, 0, root);
        using (var scope = tx.Begin()) { root.Level = 5; scope.Commit(); }
        Assert.That(root.Option.Level, Is.EqualTo(11));
        Assert.That(callbacks, Is.EqualTo(2));
        inventory.DrainChanges(new List<ItemChange<GeneratedOptionState>>());
        using (tx.Begin()) { root.Option.Level = 15; }
        Assert.That(root.Option.Level, Is.EqualTo(11));
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(callbacks, Is.EqualTo(2));
    }

    [Test]
    public void Overlapping_player_transactions_keep_undo_and_ownership_independent()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var sword = catalog.GetRequired("sword");
        var firstTx = new Transaction();
        var secondTx = new Transaction();
        var first = new ItemInventory<GeneratedOptionState>(catalog, () => 2, _ => new(), transaction: firstTx);
        var second = new ItemInventory<GeneratedOptionState>(catalog, () => 3, _ => new(), transaction: secondTx);
        var firstRoot = new GeneratedOptionState();
        var secondRoot = new GeneratedOptionState();
        first.TryLoadInstance(1, sword, 1, 0, firstRoot);
        second.TryLoadInstance(1, sword, 1, 0, secondRoot);
        second.TryGetInstance(1, out var secondItem);
        using (firstTx.Begin())
        {
            firstRoot.Level = 5;
            first.TryRemoveByInstance(1, 1);
            using (var scope = secondTx.Begin())
            {
                secondRoot.Level = 7;
                Assert.That(() => secondItem.State = firstRoot, Throws.InvalidOperationException);
                scope.Commit();
            }
            firstRoot.Level = 9;
        }
        Assert.That(firstRoot.Level, Is.EqualTo(1));
        Assert.That(secondRoot.Level, Is.EqualTo(7));
        Assert.That(first.HasChanges, Is.False);
        Assert.That(second.HasChanges, Is.True);
    }

    [Test]
    public void Null_opaque_and_generated_replacements_share_one_undo_order()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var tx = new Transaction();
        var inventory = new ItemInventory<object?>(catalog, () => 2, _ => null, transaction: tx);
        inventory.TryLoadInstance(1, catalog.GetRequired("sword"), 1, 0, null);
        inventory.TryGetInstance(1, out var item);
        var root = new GeneratedOptionState();
        using (tx.Begin())
        {
            item.State = root;
            root.Level = 5;
            item.State = new object();
            root.Option.Level = 9;
            item.State = null;
            item.State = root;
            root.Level = 8;
        }
        Assert.That(item.State, Is.Null);
        Assert.That(root.Level, Is.EqualTo(1));
        Assert.That(root.Option.Level, Is.EqualTo(2));
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(() => item.BeginStateChange(), Throws.InvalidOperationException);
        var receiver = new ItemInventory<GeneratedOptionState>(catalog, () => 3, _ => new());
        Assert.That(receiver.TryLoadInstance(2, catalog.GetRequired("sword"), 1, 0, root), Is.EqualTo(InventoryError.None));
    }

    [Test]
    public void List_and_detached_child_edits_restore_references_without_snapshot_delegate()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var tx = new Transaction();
        var root = new GeneratedOptionState();
        var child = new GeneratedOptionChild();
        root.Options.Add(child);
        var inventory = new ItemInventory<GeneratedOptionState>(catalog, () => 2, _ => new(), transaction: tx);
        inventory.TryLoadInstance(1, catalog.GetRequired("sword"), 1, 0, root);
        var added = new GeneratedOptionChild();
        using (tx.Begin())
        {
            root.Options.RemoveAt(0);
            child.Level = 9;
            root.Options.Add(added);
            added.Level = 12;
        }
        Assert.That(root.Options.Count, Is.EqualTo(1));
        Assert.That(root.Options[0], Is.SameAs(child));
        Assert.That(child.Level, Is.EqualTo(2));
        Assert.That(added.Level, Is.EqualTo(2));
        Assert.That(inventory.HasChanges, Is.False);
        added.Level = 15;
        Assert.That(inventory.HasChanges, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Created_changed_deleted_phantom_releases_all_roots(bool commit)
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var tx = new Transaction();
        var inventory = new ItemInventory<GeneratedOptionState>(catalog, () => 1, _ => new(), transaction: tx);
        GeneratedOptionState first;
        var second = new GeneratedOptionState();
        ItemInstance<GeneratedOptionState> item;
        using (var scope = tx.Begin())
        {
            inventory.TryAdd(catalog.GetRequired("sword"), 1);
            inventory.TryGetInstance(1, out item);
            first = item.State;
            first.Option.Level = 7;
            item.State = second;
            second.Level = 5;
            inventory.TryRemoveByInstance(1, 1);
            first.Level = 8;
            second.Level = 9;
            if (commit) scope.Commit();
        }
        Assert.That(first.Level, Is.EqualTo(commit ? 8 : 1));
        Assert.That(first.Option.Level, Is.EqualTo(commit ? 7 : 2));
        Assert.That(second.Level, Is.EqualTo(commit ? 9 : 1));
        inventory.DrainChanges(new List<ItemChange<GeneratedOptionState>>());
        first.Level = 20;
        second.Level = 20;
        item.State = new GeneratedOptionState();
        item.State.Level = 21;
        Assert.That(inventory.InstanceCount, Is.Zero);
        Assert.That(inventory.HasChanges, Is.False);
        var receiver = new ItemInventory<GeneratedOptionState>(catalog, () => 3, _ => new());
        Assert.That(receiver.TryLoadInstance(2, catalog.GetRequired("sword"), 1, 0, first), Is.EqualTo(InventoryError.None));
        Assert.That(receiver.TryLoadInstance(3, catalog.GetRequired("sword"), 1, 0, second), Is.EqualTo(InventoryError.None));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Removed_item_keeps_journal_until_completion(bool commit)
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var tx = new Transaction();
        var inventory = new ItemInventory<GeneratedOptionState>(catalog, () => 2, _ => new(), transaction: tx);
        var root = new GeneratedOptionState();
        inventory.TryLoadInstance(1, catalog.GetRequired("sword"), 1, 0, root);
        inventory.TryGetInstance(1, out var item);
        var other = new GeneratedOptionState();
        using (var scope = tx.Begin())
        {
            inventory.TryRemoveByInstance(1, 1);
            root.Option.Level = 7;
            item.State = other;
            other.Level = 9;
            if (commit) scope.Commit();
        }
        Assert.That(root.Option.Level, Is.EqualTo(commit ? 7 : 2));
        Assert.That(other.Level, Is.EqualTo(commit ? 9 : 1));
        Assert.That(inventory.InstanceCount, Is.EqualTo(commit ? 0 : 1));
        if (!commit) Assert.That(item.State, Is.SameAs(root));
        inventory.DrainChanges(new List<ItemChange<GeneratedOptionState>>());
        other.Level = 20;
        Assert.That(inventory.HasChanges, Is.False);
        root.Level = 20;
        Assert.That(inventory.HasChanges, Is.EqualTo(!commit));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Composite_rollback_preserves_prior_pending_changes(bool alreadyDirty)
    {
        var catalog = new ItemCatalogBuilder<string>().Register("material", "Material").Register("sword", "Sword", unstackable: true).Build();
        var material = catalog.GetRequired("material");
        var sword = catalog.GetRequired("sword");
        var tx = new Transaction();
        var dirty = 0;
        var inventory = new ItemInventory<GeneratedOptionState>(catalog, () => 3, _ => new(), transaction: tx, onDirty: () => dirty++);
        inventory.TryLoadInstance(1, material, 10, 0, new());
        var root = new GeneratedOptionState();
        var child = root.Option;
        inventory.TryLoadInstance(2, sword, 1, 0, root);
        inventory.TryGetInstance(2, out var item);
        if (alreadyDirty) item.Flags = 1;
        var beforeDirty = dirty;
        var achievementsCatalog = new AchievementCatalog<AchievementDefinition>(new[] { new AchievementDefinition("upgrade", 10) });
        var achievement = achievementsCatalog.GetRequired("upgrade");
        var achievements = new AchievementManager<AchievementDefinition>(achievementsCatalog, transaction: tx);
        using (tx.Begin())
        {
            inventory.TryRemove(material, 3);
            root.Level = 5;
            child.Level = 9;
            achievements.Increase(achievement, 1);
        }
        Assert.That(inventory.GetQuantity(material), Is.EqualTo((BigNum)10));
        Assert.That(item.State, Is.SameAs(root));
        Assert.That(root.Option, Is.SameAs(child));
        Assert.That(root.Level, Is.EqualTo(1));
        Assert.That(child.Level, Is.EqualTo(2));
        Assert.That(achievements.GetState(achievement).Progress, Is.Zero);
        Assert.That(dirty, Is.EqualTo(beforeDirty));
        Assert.That(inventory.HasChanges, Is.EqualTo(alreadyDirty));
        var changes = new List<ItemChange<GeneratedOptionState>>();
        inventory.DrainChanges(changes);
        Assert.That(changes.Count, Is.EqualTo(alreadyDirty ? 1 : 0));
    }

    [Test]
    public void Replaced_and_detached_roots_restore_values_and_original_reference()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var transaction = new Transaction();
        var inventory = new ItemInventory<GeneratedOptionState>(catalog, () => 2, _ => new(), transaction: transaction);
        var first = new GeneratedOptionState();
        var second = new GeneratedOptionState();
        inventory.TryLoadInstance(1, catalog.GetRequired("sword"), 1, 0, first);
        inventory.TryGetInstance(1, out var item);
        using (transaction.Begin())
        {
            first.Option.Level = 5;
            item.State = second;
            first.Option.Level = 8;
            second.Option.Level = 9;
        }
        Assert.That(item.State, Is.SameAs(first));
        Assert.That(first.Option.Level, Is.EqualTo(2));
        Assert.That(second.Option.Level, Is.EqualTo(2));
        Assert.That(inventory.HasChanges, Is.False);
    }
}
