using Bun3.Gameplay.Numerics;
using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class ItemInventoryTransactionTests
{
    private sealed class ItemState
    {
        public int Level = 1;
    }

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;
    private ItemId _sword;
    private ItemId _ticket;
    private long _nextId;
    private Transaction _transaction = null!;
    private ItemInventory<ItemState> _inventory = null!;
    private int _dirty;
    private readonly List<InventoryChange> _applied = new();

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "Gold")
            .Register("sword", "Sword", unstackable: true)
            .Register("ticket", "Ticket", maxCount: 5, regenPeriodTicks: TimeSpan.TicksPerMinute, maxRegen: 5)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _sword = _catalog.GetRequired("sword");
        _ticket = _catalog.GetRequired("ticket");

        _nextId = 0;
        _dirty = 0;
        _applied.Clear();
        _transaction = new Transaction();
        _inventory = new ItemInventory<ItemState>(
            _catalog,
            instanceIdIssuer: () => ++_nextId,
            stateFactory: _ => new ItemState(),
            onDirty: () => _dirty++,
            onApplied: applied =>
            {
                for (var i = 0; i < applied.Length; i++)
                {
                    _applied.Add(applied[i]);
                }
            },
            transaction: _transaction);
    }

    private static string Q(BigNum value) => value.ToString();

    [Test]
    public void Rollback_undoes_a_grant_of_a_new_stack()
    {
        using (_transaction.Begin())
        {
            Assert.That(_inventory.TryAdd(_gold, 100), Is.EqualTo(InventoryError.None));
        }

        Assert.That(_inventory.GetQuantity(_gold).Sign, Is.Zero);
        Assert.That(_inventory.InstanceCount, Is.Zero);
    }

    [Test]
    public void Rollback_restores_a_partially_consumed_stack()
    {
        _inventory.TryAdd(_gold, 100);

        using (_transaction.Begin())
        {
            Assert.That(_inventory.TryRemove(_gold, 30), Is.EqualTo(InventoryError.None));
            Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(70)));
        }

        Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(100)));
    }

    [Test]
    public void Rollback_restores_a_fully_consumed_stack_instance()
    {
        _inventory.TryAdd(_gold, 100);
        var before = _inventory.InstanceCount;

        using (_transaction.Begin())
        {
            Assert.That(_inventory.TryRemove(_gold, 100), Is.EqualTo(InventoryError.None));
            Assert.That(_inventory.InstanceCount, Is.Zero);
        }

        Assert.That(_inventory.InstanceCount, Is.EqualTo(before));
        Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(100)));
    }

    [Test]
    public void Rollback_restores_removed_unstackable_instances()
    {
        _inventory.TryAdd(_sword, 3);

        using (_transaction.Begin())
        {
            Assert.That(_inventory.TryRemove(_sword, 2), Is.EqualTo(InventoryError.None));
        }

        Assert.That(Q(_inventory.GetQuantity(_sword)), Is.EqualTo(Q(3)));
    }

    [Test]
    public void Rollback_restores_flags_and_expiry()
    {
        _inventory.TryAdd(_sword, 1);
        var buffer = new List<ItemInstance<ItemState>>();
        _inventory.CollectInstances(_sword, buffer);
        var instance = buffer[0];

        using (_transaction.Begin())
        {
            instance.Flags = 4u;
            instance.ExpiresAtTicksUtc = 12345;
        }

        Assert.That(instance.Flags, Is.Zero);
        Assert.That(instance.ExpiresAtTicksUtc, Is.Zero);
    }

    [Test]
    public void Commit_keeps_changes()
    {
        using (var scope = _transaction.Begin())
        {
            _inventory.TryAdd(_gold, 100);
            _inventory.TryRemove(_gold, 40);
            scope.Commit();
        }

        Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(60)));
    }

    [Test]
    public void Notifications_are_dropped_on_rollback()
    {
        using (_transaction.Begin())
        {
            _inventory.TryAdd(_gold, 100);
            Assert.That(_dirty, Is.Zero, "onDirty must be buffered inside a transaction");
            Assert.That(_applied, Is.Empty, "onApplied must be buffered inside a transaction");
        }

        Assert.That(_dirty, Is.Zero);
        Assert.That(_applied, Is.Empty);
    }

    [Test]
    public void Notifications_fire_once_on_commit_with_all_changes()
    {
        using (var scope = _transaction.Begin())
        {
            _inventory.TryAdd(_gold, 100);
            _inventory.TryRemove(_gold, 40);
            scope.Commit();
        }

        Assert.That(_dirty, Is.EqualTo(1));
        Assert.That(_applied.Count, Is.EqualTo(2));
        Assert.That(Q(_applied[0].Delta), Is.EqualTo(Q(100)));
        Assert.That(Q(_applied[1].Delta), Is.EqualTo(Q(-40)));
        Assert.That(Q(_applied[1].Balance), Is.EqualTo(Q(60)));
    }

    [Test]
    public void Outside_a_transaction_notifications_stay_immediate()
    {
        _inventory.TryAdd(_gold, 100);

        Assert.That(_dirty, Is.EqualTo(1));
        Assert.That(_applied.Count, Is.EqualTo(1));
    }

    [Test]
    public void Rollback_reverts_pending_removals_for_change_tracking()
    {
        _inventory.TryAdd(_gold, 100);
        var drained = new List<ItemChange<ItemState>>();
        _inventory.DrainChanges(drained);
        drained.Clear();

        using (_transaction.Begin())
        {
            _inventory.TryRemove(_gold, 100);
        }

        _inventory.DrainChanges(drained);
        Assert.That(drained, Is.Empty, "a rolled-back removal must not surface as a Removed change");
    }

    [Test]
    public void Rollback_restores_the_regen_basis()
    {
        var start = TimeSpan.TicksPerMinute * 100;
        _inventory.SettleRegen(start);                       // initializes the basis
        var basisBefore = _inventory.GetRegenBasis(_ticket);
        var quantityBefore = Q(_inventory.GetQuantity(_ticket));

        using (_transaction.Begin())
        {
            _inventory.SettleRegen(start + TimeSpan.TicksPerMinute * 30);
            Assert.That(_inventory.GetRegenBasis(_ticket), Is.Not.EqualTo(basisBefore));
        }

        Assert.That(_inventory.GetRegenBasis(_ticket), Is.EqualTo(basisBefore));
        Assert.That(Q(_inventory.GetQuantity(_ticket)), Is.EqualTo(quantityBefore));
    }

    [Test]
    public void Rollback_undoes_a_mixed_batch_atomically()
    {
        _inventory.TryAdd(_gold, 500);
        _inventory.TryAdd(_sword, 2);

        using (_transaction.Begin())
        {
            var tx = _inventory.BeginTransaction();
            tx.Remove(_gold, 200);
            tx.Add(_sword, 1);
            Assert.That(tx.Commit(out _), Is.EqualTo(InventoryError.None));
            Assert.That(Q(_inventory.GetQuantity(_sword)), Is.EqualTo(Q(3)));
        }

        Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(500)));
        Assert.That(Q(_inventory.GetQuantity(_sword)), Is.EqualTo(Q(2)));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(3));   // gold singleton + 2 swords
    }

    [Test]
    public void Transfer_inside_a_transaction_throws()
    {
        long otherNext = 1000;
        var other = new ItemInventory<ItemState>(
            _catalog,
            instanceIdIssuer: () => ++otherNext,
            stateFactory: _ => new ItemState());
        _inventory.TryAdd(_gold, 100);

        using (_transaction.Begin())
        {
            Assert.That(() => _inventory.TryTransfer(other, _gold, 10), Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public void Transfer_outside_a_transaction_still_works()
    {
        long otherNext = 1000;
        var other = new ItemInventory<ItemState>(
            _catalog,
            instanceIdIssuer: () => ++otherNext,
            stateFactory: _ => new ItemState());
        _inventory.TryAdd(_gold, 100);

        Assert.That(_inventory.TryTransfer(other, _gold, 40), Is.EqualTo(InventoryError.None));
        Assert.That(Q(_inventory.GetQuantity(_gold)), Is.EqualTo(Q(60)));
        Assert.That(Q(other.GetQuantity(_gold)), Is.EqualTo(Q(40)));
    }

    [Test]
    public void Rollback_after_create_then_remove_leaves_nothing()
    {
        using (_transaction.Begin())
        {
            _inventory.TryAdd(_gold, 100);
            _inventory.TryRemove(_gold, 100);
        }

        Assert.That(_inventory.GetQuantity(_gold).Sign, Is.Zero);
        Assert.That(_inventory.InstanceCount, Is.Zero);
    }

    [Test]
    public void Rollback_after_create_then_partial_remove_leaves_nothing()
    {
        using (_transaction.Begin())
        {
            _inventory.TryAdd(_sword, 2);
            _inventory.TryRemove(_sword, 1);
        }

        Assert.That(_inventory.GetQuantity(_sword).Sign, Is.Zero);
        Assert.That(_inventory.InstanceCount, Is.Zero);
    }

    [Test]
    public void DrainChanges_inside_a_transaction_throws()
    {
        using (_transaction.Begin())
        {
            var buffer = new List<ItemChange<ItemState>>();
            Assert.That(() => _inventory.DrainChanges(buffer), Throws.TypeOf<InvalidOperationException>());
        }
    }
}
