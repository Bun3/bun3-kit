using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class ItemStateRollbackTests
{
    private sealed class MutableState
    {
        public int Level;
        public List<int> Options = new();

        public MutableState Clone() => new MutableState { Level = Level, Options = new List<int>(Options) };
    }

    private readonly record struct ValueState(int Level);

    private ItemCatalog<string> _catalog = null!;
    private ItemId _sword;
    private Transaction _transaction = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("sword", "Sword", unstackable: true)
            .Build();
        _sword = _catalog.GetRequired("sword");
        _transaction = new Transaction();
    }

    private ItemInventory<T> Inventory<T>(Func<ItemId, T> factory, Func<T, T>? snapshot = null)
    {
        long next = 0;
        return new ItemInventory<T>(
            _catalog,
            instanceIdIssuer: () => ++next,
            stateFactory: factory,
            transaction: _transaction,
            snapshotState: snapshot);
    }

    private static ItemInstance<T> Single<T>(ItemInventory<T> inventory, ItemId item)
    {
        var buffer = new List<ItemInstance<T>>();
        inventory.CollectInstances(item, buffer);
        return buffer[0];
    }

    [Test]
    public void Value_state_rolls_back_through_the_setter()
    {
        var inventory = Inventory<ValueState>(_ => new ValueState(1));
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (_transaction.Begin())
        {
            instance.State = instance.State with { Level = 9 };
            Assert.That(instance.State.Level, Is.EqualTo(9));
        }

        Assert.That(instance.State.Level, Is.EqualTo(1));
    }

    [Test]
    public void Value_state_setter_commits()
    {
        var inventory = Inventory<ValueState>(_ => new ValueState(1));
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (var scope = _transaction.Begin())
        {
            instance.State = instance.State with { Level = 9 };
            scope.Commit();
        }

        Assert.That(instance.State.Level, Is.EqualTo(9));
    }

    [Test]
    public void Mutable_state_rolls_back_through_the_change_scope()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState { Level = 1 }, s => s.Clone());
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (_transaction.Begin())
        {
            using (var scope = instance.BeginStateChange())
            {
                scope.State.Level = 9;
                scope.State.Options.Add(42);
            }

            Assert.That(instance.State.Level, Is.EqualTo(9));
        }

        Assert.That(instance.State.Level, Is.EqualTo(1));
        Assert.That(instance.State.Options, Is.Empty);
    }

    [Test]
    public void Change_scope_marks_the_instance_changed()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState(), s => s.Clone());
        inventory.TryAdd(_sword, 1);
        var drained = new List<ItemChange<MutableState>>();
        inventory.DrainChanges(drained);
        drained.Clear();
        var instance = Single(inventory, _sword);

        using (var scope = instance.BeginStateChange())
        {
            scope.State.Level = 3;
        }

        inventory.DrainChanges(drained);
        Assert.That(drained.Count, Is.EqualTo(1));
        Assert.That(drained[0].Kind, Is.EqualTo(ItemChangeKind.Updated));
    }

    [Test]
    public void Change_scope_without_snapshot_delegate_throws_inside_a_transaction()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState());
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (_transaction.Begin())
        {
            Assert.That(() => instance.BeginStateChange(), Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public void Change_scope_without_snapshot_delegate_works_outside_a_transaction()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState());
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (var scope = instance.BeginStateChange())
        {
            scope.State.Level = 5;
        }

        Assert.That(instance.State.Level, Is.EqualTo(5));
    }

    [Test]
    public void MarkChanged_inside_a_transaction_throws()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState(), s => s.Clone());
        inventory.TryAdd(_sword, 1);
        var instance = Single(inventory, _sword);

        using (_transaction.Begin())
        {
            Assert.That(() => instance.MarkChanged(), Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public void MarkChanged_outside_a_transaction_still_works()
    {
        var inventory = Inventory<MutableState>(_ => new MutableState());
        inventory.TryAdd(_sword, 1);
        var drained = new List<ItemChange<MutableState>>();
        inventory.DrainChanges(drained);
        drained.Clear();
        var instance = Single(inventory, _sword);

        instance.State.Level = 7;
        instance.MarkChanged();

        inventory.DrainChanges(drained);
        Assert.That(drained.Count, Is.EqualTo(1));
        Assert.That(drained[0].Kind, Is.EqualTo(ItemChangeKind.Updated));
    }

    [Test]
    public void MarkChanged_guard_fires_before_any_side_effect()
    {
        var dirty = 0;
        long next = 0;
        var inventory = new ItemInventory<MutableState>(
            _catalog,
            instanceIdIssuer: () => ++next,
            stateFactory: _ => new MutableState(),
            onDirty: () => dirty++,
            transaction: _transaction);
        inventory.TryAdd(_sword, 1);
        var drained = new List<ItemChange<MutableState>>();
        inventory.DrainChanges(drained);
        dirty = 0;
        var instance = Single(inventory, _sword);

        using (_transaction.Begin())
        {
            Assert.That(() => instance.MarkChanged(), Throws.TypeOf<InvalidOperationException>());
        }

        Assert.That(dirty, Is.Zero, "the guard must throw before onDirty fires");
        Assert.That(inventory.HasChanges, Is.False, "the guard must throw before HasChanges is set");
    }
}
