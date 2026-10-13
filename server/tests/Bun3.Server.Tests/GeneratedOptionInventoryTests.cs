using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class GeneratedOptionInventoryTests
{
    [Test]
    public void Invalid_generated_assignment_does_not_disable_opaque_scopes()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var sword = catalog.GetRequired("sword");
        var root = new GeneratedOptionState();
        var holder = new ItemInventory<GeneratedOptionState>(catalog, () => 3, _ => new());
        holder.TryLoadInstance(1, sword, 1, 0, root);
        var tx = new Transaction();
        var original = new object();
        var inventory = new ItemInventory<object>(catalog, () => 4, _ => new object(), transaction: tx, snapshotState: x => x);
        inventory.TryLoadInstance(2, sword, 1, 0, original);
        inventory.TryGetInstance(2, out var item);
        using (tx.Begin())
        {
            Assert.That(() => item.State = root, Throws.InvalidOperationException);
            Assert.That(item.State, Is.SameAs(original));
            Assert.That(item.TxSnapshotIndex, Is.EqualTo(-1));
            Assert.That(inventory.HasChanges, Is.False);
            Assert.DoesNotThrow(() => { using var edit = item.BeginStateChange(); });
        }
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(holder.HasChanges, Is.False);
    }

    [Test]
    public void Failed_opaque_scope_does_not_disable_generated_assignment()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var tx = new Transaction();
        var original = new object();
        var inventory = new ItemInventory<object>(catalog, () => 2, _ => new object(), transaction: tx);
        inventory.TryLoadInstance(1, catalog.GetRequired("sword"), 1, 0, original);
        inventory.TryGetInstance(1, out var item);
        var root = new GeneratedOptionState();
        using (tx.Begin())
        {
            Assert.That(() => item.BeginStateChange(), Throws.InvalidOperationException);
            Assert.That(item.TxSnapshotIndex, Is.EqualTo(-1));
            Assert.That(inventory.HasChanges, Is.False);
            Assert.DoesNotThrow(() => item.State = root);
            root.Level = 9;
        }
        Assert.That(item.State, Is.SameAs(original));
        Assert.That(root.Level, Is.EqualTo(1));
        Assert.That(inventory.HasChanges, Is.False);
    }

    [Test]
    public void Scope_used_in_rolled_back_transaction_still_prohibits_generated_migration()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var tx = new Transaction();
        var inventory = new ItemInventory<object>(catalog, () => 2, _ => new object(), transaction: tx, snapshotState: _ => new object());
        inventory.TryLoadInstance(1, catalog.GetRequired("sword"), 1, 0, new object());
        inventory.TryGetInstance(1, out var item);
        var root = new GeneratedOptionState();
        using (tx.Begin())
        {
            using (item.BeginStateChange())
            {
                Assert.That(() => item.State = root, Throws.InvalidOperationException);
            }
        }
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(() => item.State = root, Throws.InvalidOperationException);
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(item.TxSnapshotIndex, Is.EqualTo(-1));
        var receiver = new ItemInventory<GeneratedOptionState>(catalog, () => 3, _ => new());
        Assert.That(receiver.TryLoadInstance(2, catalog.GetRequired("sword"), 1, 0, root), Is.EqualTo(InventoryError.None));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Generated_history_rejects_scope_after_null_or_opaque_replacement(bool useNull)
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var tx = new Transaction();
        var inventory = new ItemInventory<object?>(catalog, () => 1, _ => new GeneratedOptionState(), transaction: tx, snapshotState: x => x);
        inventory.TryAdd(catalog.GetRequired("sword"), 1);
        inventory.TryGetInstance(1, out var item);
        using (var scope = tx.Begin())
        {
            item.State = useNull ? null : new object();
            Assert.That(() => item.BeginStateChange(), Throws.InvalidOperationException);
            scope.Commit();
        }
        Assert.That(() => item.BeginStateChange(), Throws.InvalidOperationException);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Scope_history_rejects_generated_root_before_any_bookkeeping(bool keepScopeOpen)
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var tx = new Transaction();
        var inventory = new ItemInventory<object?>(catalog, () => 1, _ => new object(), transaction: tx, snapshotState: x => x);
        inventory.TryAdd(catalog.GetRequired("sword"), 1);
        inventory.TryGetInstance(1, out var item);
        var original = item.State;
        var edit = item.BeginStateChange();
        if (!keepScopeOpen) edit.Dispose();
        inventory.DrainChanges(new List<ItemChange<object?>>());
        var root = new GeneratedOptionState();
        using (tx.Begin())
        {
            Assert.That(() => item.State = root, Throws.InvalidOperationException);
            Assert.That(item.State, Is.SameAs(original));
            Assert.That(inventory.HasChanges, Is.False);
            Assert.That(item.TxSnapshotIndex, Is.EqualTo(-1));
        }
        root.Level = 4;
        Assert.That(inventory.HasChanges, Is.False);
        if (keepScopeOpen) edit.Dispose();
    }

    [Test]
    public void Shared_root_load_and_replacement_fail_without_enlisting_or_mutating()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var id = catalog.GetRequired("sword");
        var tx = new Transaction();
        var inventory = new ItemInventory<GeneratedOptionState>(catalog, () => 3, _ => new(), transaction: tx);
        var root = new GeneratedOptionState();
        inventory.TryLoadInstance(1, id, 1, 0, root);
        Assert.That(() => inventory.TryLoadInstance(2, id, 1, 0, root), Throws.InvalidOperationException);
        Assert.That(inventory.InstanceCount, Is.EqualTo(1));
        var other = new GeneratedOptionState();
        inventory.TryLoadInstance(2, id, 1, 0, other);
        inventory.TryGetInstance(2, out var item);
        using (tx.Begin())
        {
            Assert.That(() => item.State = root, Throws.InvalidOperationException);
            Assert.That(item.TxSnapshotIndex, Is.EqualTo(-1));
            Assert.That(item.State, Is.SameAs(other));
            Assert.That(inventory.HasChanges, Is.False);
        }
        root.Option.Level = 6;
        var changes = new List<ItemChange<GeneratedOptionState>>();
        inventory.DrainChanges(changes);
        Assert.That(changes.Select(x => x.InstanceId), Is.EqualTo(new[] { 1L }));
    }

    [Test]
    public void Transfer_routes_edits_to_destination_and_reuses_transaction_safely()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var tx = new Transaction();
        var source = new ItemInventory<GeneratedOptionState>(catalog, () => 1, _ => new(), transaction: tx);
        var target = new ItemInventory<GeneratedOptionState>(catalog, () => 2, _ => new(), transaction: tx);
        source.TryAdd(catalog.GetRequired("sword"), 1);
        source.TryGetInstance(1, out var item);
        using (var scope = tx.Begin()) { item.State.Level = 3; scope.Commit(); }
        Assert.That(source.TryTransferByInstance(target, 1), Is.EqualTo(InventoryError.None));
        source.DrainChanges(new List<ItemChange<GeneratedOptionState>>());
        target.DrainChanges(new List<ItemChange<GeneratedOptionState>>());
        using (tx.Begin())
        {
            item.State.Level = 8;
            Assert.That(() => target.TryTransferByInstance(source, 1), Throws.InvalidOperationException);
        }
        Assert.That(item.State.Level, Is.EqualTo(3));
        Assert.That(source.HasChanges, Is.False);
        Assert.That(target.HasChanges, Is.False);
        item.State.Level = 9;
        Assert.That(source.HasChanges, Is.False);
        Assert.That(target.HasChanges, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Nested_edit_marks_loaded_item_without_snapshot_delegate(bool withTransaction)
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var id = catalog.GetRequired("sword");
        var dirty = 0;
        var snapshots = 0;
        var transaction = withTransaction ? new Transaction() : null;
        var inventory = new ItemInventory<GeneratedOptionState>(catalog, () => 2, _ => new(),
            onDirty: () => dirty++, transaction: transaction, snapshotState: state => { snapshots++; return state; });
        var state = new GeneratedOptionState();
        Assert.That(inventory.TryLoadInstance(1, id, 1, 0, state), Is.EqualTo(InventoryError.None));
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(dirty, Is.Zero);
        state.Option.Level = 9;
        var changes = new List<ItemChange<GeneratedOptionState>>();
        inventory.DrainChanges(changes);
        Assert.That(changes.Select(c => c.Kind), Is.EqualTo(new[] { ItemChangeKind.Updated }));
        Assert.That(snapshots, Is.Zero);
        Assert.That(dirty, Is.EqualTo(1));
    }

    [Test]
    public void Generated_state_rejects_deep_copy_scope_and_same_root_is_noop()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var inventory = new ItemInventory<GeneratedOptionState>(catalog, () => 1, _ => new());
        inventory.TryAdd(catalog.GetRequired("sword"), 1);
        inventory.TryGetInstance(1, out var item);
        inventory.DrainChanges(new List<ItemChange<GeneratedOptionState>>());
        Assert.That(() => item.BeginStateChange(), Throws.InvalidOperationException);
        item.State = item.State;
        Assert.That(inventory.HasChanges, Is.False);
    }
}
