using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class InventoryTransactionTests
{
    private sealed class ItemState;

    private const uint FlagLocked = 1u << 0;

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;      // stackable
    private ItemId _sword;     // unstackable
    private long _nextId;
    private ItemInventory<ItemState> _inventory = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "Gold")
            .Register("sword", "Sword", unstackable: true)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _sword = _catalog.GetRequired("sword");
        _nextId = 0;
        _inventory = new ItemInventory<ItemState>(
            _catalog,
            instanceIdIssuer: () => ++_nextId,
            stateFactory: _ => new ItemState(),
            removeBlockingFlags: FlagLocked);
    }

    [Test]
    public void Commit_mixes_definition_and_instance_ops_atomically()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_sword, 2, created);   // swords #1, #2
        _inventory.TryAdd(_gold, 100);

        // Destroy a specific sword + spend gold + grant a new sword — a batch TryApply cannot express
        var tx = _inventory.BeginTransaction();
        tx.RemoveInstance(created[0].InstanceId);
        tx.Remove(_gold, 30);
        tx.Add(_sword, 1);

        Assert.That(tx.Commit(out _, created), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.TryGetInstance(created[0].InstanceId, out _), Is.False);
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)70));
        Assert.That(_inventory.GetQuantity(_sword), Is.EqualTo((BigNum)2));
    }

    [Test]
    public void Targeted_instances_are_excluded_from_definition_pool()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_sword, 2, created);   // 2 swords

        // If a definition-level removal (1) picked the targeted instance, the targeted removal
        // would break — it must be excluded from the pool. Ordering definition removal before
        // the instance target verifies the exclusion is global, not order-dependent.
        var tx = _inventory.BeginTransaction();
        tx.Remove(_sword, 1);
        tx.RemoveInstance(created[0].InstanceId);

        Assert.That(tx.Commit(out _), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(0));

        // With only 1 sword, demanding both targeted and definition removal is insufficient
        _inventory.TryAdd(_sword, 1, created);
        var tx2 = _inventory.BeginTransaction();
        tx2.RemoveInstance(created[^1].InstanceId);
        tx2.Remove(_sword, 1);
        Assert.That(tx2.Commit(out var failedIndex), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(1));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(1), "failed batch leaves no changes");
    }

    [Test]
    public void Singleton_remove_instance_settles_against_definition_pool()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_gold, 100, created);
        var goldId = created[0].InstanceId;

        // Partial instance removal and definition removal settle against the same pool
        var tx = _inventory.BeginTransaction();
        tx.RemoveInstance(goldId, 60);
        tx.Remove(_gold, 50);   // remaining 40 < 50
        Assert.That(tx.Commit(out var failedIndex), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(1));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)100));

        // Full-instance removal (RemoveInstanceAll) settles the entire remainder
        var tx2 = _inventory.BeginTransaction();
        tx2.Remove(_gold, 40);
        tx2.RemoveInstance(goldId);   // all remaining 60
        Assert.That(tx2.Commit(out _), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(0));
    }

    [Test]
    public void Duplicate_target_and_locked_target_fail()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_sword, 2, created);

        var tx = _inventory.BeginTransaction();
        tx.RemoveInstance(created[0].InstanceId);
        tx.RemoveInstance(created[0].InstanceId);   // same instance targeted twice
        Assert.That(tx.Commit(out var failedIndex), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(1));

        created[1].Flags = FlagLocked;
        var tx2 = _inventory.BeginTransaction();
        tx2.RemoveInstance(created[1].InstanceId);
        Assert.That(tx2.Commit(out _), Is.EqualTo(InventoryError.Locked));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(2), "failed batch leaves no changes");
    }

    [Test]
    public void Stale_or_committed_builders_are_rejected()
    {
        var tx = _inventory.BeginTransaction();
        tx.Add(_gold, 1);

        var tx2 = _inventory.BeginTransaction();   // discards the previous batch
        Assert.That(() => tx.Add(_gold, 1), Throws.InvalidOperationException);

        tx2.Add(_gold, 5);
        Assert.That(tx2.Commit(out _), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)5), "discarded batch ops are not applied");
        Assert.That(() => tx2.Commit(out _), Throws.InvalidOperationException, "no reuse after commit");
    }

    [Test]
    public void Commit_does_not_allocate_without_instance_creation()
    {
        _inventory.TryAdd(_gold, 1_000_000);

        for (var i = 0; i < 3; i++)   // warmup
        {
            var warm = _inventory.BeginTransaction();
            warm.Add(_gold, 10);
            warm.Remove(_gold, 10);
            warm.Commit(out _);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            var tx = _inventory.BeginTransaction();
            tx.Add(_gold, 10);
            tx.Remove(_gold, 10);
            tx.Commit(out _);
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
    }
}
