using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class ItemInventoryTests
{
    private sealed class ItemState
    {
        public int Level = 1;
    }

    private const uint FlagInUse = 1u << 0;
    private const uint FlagUserLocked = 1u << 1;

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;      // stackable, unbounded
    private ItemId _potion;    // stackable, maxCount 10
    private ItemId _sword;     // unstackable, max 3 held
    private ItemId _relic;     // unstackable, unbounded

    private long _nextId;
    private ItemInventory<ItemState> _inventory = null!;
    private int _changed;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "Gold")
            .Register("potion", "Potion", maxCount: 10)
            .Register("sword", "Sword", maxCount: 3, unstackable: true)
            .Register("relic", "Relic", unstackable: true)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _potion = _catalog.GetRequired("potion");
        _sword = _catalog.GetRequired("sword");
        _relic = _catalog.GetRequired("relic");

        _nextId = 0;
        _changed = 0;
        _inventory = new ItemInventory<ItemState>(
            _catalog,
            instanceIdIssuer: () => ++_nextId,
            stateFactory: _ => new ItemState(),
            onChanged: () => _changed++,
            removeBlockingFlags: FlagInUse | FlagUserLocked);
    }

    // ---- Stackable ----

    [Test]
    public void Stackable_merges_into_singleton_instance()
    {
        Assert.That(_inventory.TryAdd(_gold, 100), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.TryAdd(_gold, 50), Is.EqualTo(InventoryError.None));

        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)150));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(1));
        Assert.That(_inventory.TryRemove(_gold, 150), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(0));
    }

    [Test]
    public void Stackable_respects_max_stack_and_insufficient()
    {
        _inventory.TryAdd(_potion, 8);

        Assert.That(_inventory.TryAdd(_potion, 3), Is.EqualTo(InventoryError.ExceedsMaxCount));
        Assert.That(_inventory.TryRemove(_potion, 9), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(_inventory.GetQuantity(_potion), Is.EqualTo((BigNum)8));
    }

    // ---- BigNum quantities (currency = item row) ----

    [Test]
    public void Stackable_holds_astronomical_quantities()
    {
        var huge = BigNum.FromParts(5, 1000);   // 5e1000

        Assert.That(_inventory.TryAdd(_gold, huge), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.TryRemove(_gold, BigNum.FromParts(6, 1000)), Is.EqualTo(InventoryError.Insufficient));
        // Full drain — BigNum subtraction returns exactly Zero and the entry is removed
        Assert.That(_inventory.TryRemove(_gold, huge), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(0));
    }

    [Test]
    public void Stackable_addition_is_lossy_by_contract()
    {
        var huge = BigNum.FromParts(1, 30);   // 1e30

        _inventory.TryAdd(_gold, huge);
        _inventory.TryAdd(_gold, 1);          // beyond significant digits — absorbed

        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo(huge));
    }

    [Test]
    public void Unstackable_amounts_must_be_small_integers()
    {
        Assert.That(_inventory.TryAdd(_relic, 1001), Is.EqualTo(InventoryError.InvalidAmount));
        Assert.That(_inventory.TryAdd(_relic, (BigNum)0.5), Is.EqualTo(InventoryError.InvalidAmount));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(0));
    }

    // ---- Unstackable ----

    [Test]
    public void Unstackable_grant_creates_quantity_one_instances()
    {
        var created = new List<ItemInstance<ItemState>>();

        Assert.That(_inventory.TryAdd(_sword, 2, created), Is.EqualTo(InventoryError.None));
        Assert.That(created, Has.Count.EqualTo(2));
        Assert.That(created[0].Quantity, Is.EqualTo(BigNum.One));
        Assert.That(created[0].InstanceId, Is.Not.EqualTo(created[1].InstanceId));
        Assert.That(_inventory.GetQuantity(_sword), Is.EqualTo((BigNum)2));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(2));
    }

    [Test]
    public void Unstackable_max_stack_bounds_held_instances()
    {
        _inventory.TryAdd(_sword, 3);

        Assert.That(_inventory.TryAdd(_sword, 1), Is.EqualTo(InventoryError.ExceedsMaxCount));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(3));
    }

    // ---- Locking ----

    [Test]
    public void Locked_instances_are_excluded_from_removal()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_sword, 2, created);
        created[0].Flags = FlagInUse;

        Assert.That(_inventory.TryRemove(_sword, 2), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(_inventory.TryRemove(_sword, 1), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.TryGetInstance(created[0].InstanceId, out _), Is.True,
            "locked instance must remain");
        Assert.That(_inventory.TryRemoveByInstance(created[0].InstanceId, 1), Is.EqualTo(InventoryError.Locked));
    }

    [Test]
    public void Locked_stack_singleton_blocks_consumption_but_not_grant()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_gold, 100, created);
        created[0].Flags = FlagUserLocked;

        Assert.That(_inventory.TryRemove(_gold, 1), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(_inventory.TryAdd(_gold, 50), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)150));
    }

    [Test]
    public void RemoveByInstance_consumes_partial_stack_or_whole_instance()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_gold, 100, created);
        _inventory.TryAdd(_sword, 1, created);

        Assert.That(_inventory.TryRemoveByInstance(created[0].InstanceId, 40), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)60));
        Assert.That(_inventory.TryRemoveByInstance(created[1].InstanceId, 1), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_sword), Is.EqualTo(BigNum.Zero));
        Assert.That(_inventory.TryRemoveByInstance(9999, 1), Is.EqualTo(InventoryError.UnknownInstance));
    }

    // ---- Transactions (delta span path) ----

    [Test]
    public void Apply_mixes_stack_and_instance_deltas_atomically()
    {
        _inventory.TryAdd(_gold, 500);
        var created = new List<ItemInstance<ItemState>>();

        Span<ItemDelta> deltas = stackalloc ItemDelta[3];
        deltas[0] = new ItemDelta(_gold, -300);
        deltas[1] = new ItemDelta(_sword, 1);
        deltas[2] = new ItemDelta(_potion, 5);

        Assert.That(_inventory.TryApply(deltas, out _, created), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)200));
        Assert.That(_inventory.GetQuantity(_sword), Is.EqualTo(BigNum.One));
        Assert.That(_inventory.GetQuantity(_potion), Is.EqualTo((BigNum)5));
        Assert.That(created, Has.Count.EqualTo(2));   // sword instance + potion singleton
    }

    [Test]
    public void Apply_failure_leaves_inventory_untouched_and_skips_id_issuer()
    {
        _inventory.TryAdd(_gold, 100);
        var idsBefore = _nextId;

        Span<ItemDelta> deltas = stackalloc ItemDelta[3];
        deltas[0] = new ItemDelta(_sword, 2);
        deltas[1] = new ItemDelta(_gold, -50);
        deltas[2] = new ItemDelta(_gold, -60);    // remaining 50 < 60

        Assert.That(_inventory.TryApply(deltas, out var failedIndex), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(2));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)100));
        Assert.That(_inventory.GetQuantity(_sword), Is.EqualTo(BigNum.Zero));
        Assert.That(_nextId, Is.EqualTo(idsBefore), "failed batch must not call the id issuer");
    }

    [Test]
    public void Apply_accumulates_duplicate_unstackable_deltas()
    {
        Span<ItemDelta> deltas = stackalloc ItemDelta[2];
        deltas[0] = new ItemDelta(_sword, 2);
        deltas[1] = new ItemDelta(_sword, 2);   // cumulative 4 > max 3

        Assert.That(_inventory.TryApply(deltas, out var failedIndex), Is.EqualTo(InventoryError.ExceedsMaxCount));
        Assert.That(failedIndex, Is.EqualTo(1));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(0));
    }

    [Test]
    public void Apply_rejects_zero_delta_and_empty_batch_is_success()
    {
        var container = _inventory;

        Span<ItemDelta> deltas = stackalloc ItemDelta[1];
        deltas[0] = new ItemDelta(_gold, 0);
        Assert.That(container.TryApply(deltas, out var failedIndex), Is.EqualTo(InventoryError.InvalidAmount));
        Assert.That(failedIndex, Is.EqualTo(0));

        Assert.That(container.TryApply(ReadOnlySpan<ItemDelta>.Empty, out _), Is.EqualTo(InventoryError.None));
    }

    // ---- Change tracking ----

    [Test]
    public void Drain_reports_created_updated_removed()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_gold, 100, created);
        _inventory.TryAdd(_sword, 1, created);
        var buffer = new List<ItemChange<ItemState>>();
        _inventory.DrainChanges(buffer);
        Assert.That(buffer.Select(c => c.Kind), Is.All.EqualTo(ItemChangeKind.Created));
        Assert.That(buffer, Has.Count.EqualTo(2));
        Assert.That(_inventory.HasChanges, Is.False);

        created[0].State.Level = 5;
        created[0].MarkChanged();
        _inventory.TryRemoveByInstance(created[1].InstanceId, 1);

        buffer.Clear();
        _inventory.DrainChanges(buffer);
        Assert.That(buffer, Has.Count.EqualTo(2));
        Assert.That(buffer[0].Kind, Is.EqualTo(ItemChangeKind.Removed));
        Assert.That(buffer[0].InstanceId, Is.EqualTo(created[1].InstanceId));
        Assert.That(buffer[1].Kind, Is.EqualTo(ItemChangeKind.Updated));
        Assert.That(buffer[1].Instance, Is.SameAs(created[0]));
    }

    [Test]
    public void Created_then_removed_before_drain_cancels_out()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_sword, 1, created);
        _inventory.TryRemoveByInstance(created[0].InstanceId, 1);

        var buffer = new List<ItemChange<ItemState>>();
        _inventory.DrainChanges(buffer);
        Assert.That(buffer, Is.Empty, "never-persisted instance must not emit a DELETE");
    }

    [Test]
    public void Flags_setter_and_mark_changed_notify_on_changed()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_sword, 1, created);
        var baseline = _changed;

        created[0].Flags = FlagInUse;    // +1
        created[0].Flags = FlagInUse;    // same value — no notification
        created[0].MarkChanged();        // +1

        Assert.That(_changed, Is.EqualTo(baseline + 2));
    }

    // ---- Loading ----

    [Test]
    public void Load_accepts_external_ids_without_tracking()
    {
        Assert.That(_inventory.TryLoadInstance(777, _gold, 500, 0, new ItemState()), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.TryLoadInstance(778, _sword, 1, FlagInUse, new ItemState()), Is.EqualTo(InventoryError.None));

        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)500));
        Assert.That(_inventory.HasChanges, Is.False);
        Assert.That(_changed, Is.EqualTo(0));

        Assert.That(_inventory.TryAdd(_gold, 1), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)501));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(2));
    }

    [Test]
    public void Load_rejects_duplicates_and_invalid_quantities()
    {
        _inventory.TryLoadInstance(1, _gold, 10, 0, new ItemState());

        Assert.That(_inventory.TryLoadInstance(1, _sword, 1, 0, new ItemState()),
            Is.EqualTo(InventoryError.DuplicateInstance));
        Assert.That(_inventory.TryLoadInstance(2, _gold, 10, 0, new ItemState()),
            Is.EqualTo(InventoryError.DuplicateInstance), "second instance of a stackable definition");
        Assert.That(_inventory.TryLoadInstance(3, _sword, 2, 0, new ItemState()),
            Is.EqualTo(InventoryError.InvalidAmount), "unstackable allows quantity 1 only");
        Assert.That(_inventory.TryLoadInstance(4, _potion, 11, 0, new ItemState()),
            Is.EqualTo(InventoryError.ExceedsMaxCount));
    }

    // ---- Zero allocation ----

    [Test]
    public void Query_and_stack_paths_do_not_allocate()
    {
        _inventory.TryAdd(_gold, 1000);
        _inventory.TryAdd(_sword, 2);
        Span<ItemDelta> deltas = stackalloc ItemDelta[2];
        deltas[0] = new ItemDelta(_gold, 10);
        deltas[1] = new ItemDelta(_gold, -10);

        for (var i = 0; i < 3; i++)   // warmup (incl. enumeration — absorbs Dictionary.Values one-time lazy alloc)
        {
            _inventory.GetQuantity(_gold);
            _inventory.TryApply(deltas, out _);
            foreach (var instance in _inventory)
            {
                _ = instance.Quantity;
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _inventory.GetQuantity(_gold);
            _inventory.GetQuantity(_sword);
            _inventory.TryApply(deltas, out _);
            _inventory.TryGetInstance(1, out _);
            foreach (var instance in _inventory)
            {
                _ = instance.Quantity;
            }
        }

        // Compare outside NUnit: in Release the JIT can sink the counter read past
        // Is.EqualTo's own constraint allocation, counting NUnit's 24-byte object.
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0) Assert.Fail($"allocated {allocated} bytes across 1000 iterations");
    }
}
