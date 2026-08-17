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
    private ItemId _gold;      // 스택형, 무제한
    private ItemId _potion;    // 스택형, maxStack 10
    private ItemId _sword;     // 비스택형, 최대 3개 보유
    private ItemId _relic;     // 비스택형, 무제한

    private long _nextId;
    private ItemInventory<ItemState> _inventory = null!;
    private int _changed;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "골드")
            .Register("potion", "물약", maxStack: 10)
            .Register("sword", "검", maxStack: 3, unstackable: true)
            .Register("relic", "유물", unstackable: true)
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

    // ---- 판정 내부 흡수: 스택형 ----

    [Test]
    public void Stackable_merges_into_singleton_instance()
    {
        Assert.That(_inventory.TryAdd(_gold, 100), Is.EqualTo(ItemError.None));
        Assert.That(_inventory.TryAdd(_gold, 50), Is.EqualTo(ItemError.None));

        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo(150));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(1));
        Assert.That(_inventory.TryRemove(_gold, 150), Is.EqualTo(ItemError.None));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(0));
    }

    [Test]
    public void Stackable_respects_max_stack_and_insufficient()
    {
        _inventory.TryAdd(_potion, 8);

        Assert.That(_inventory.TryAdd(_potion, 3), Is.EqualTo(ItemError.ExceedsMaxStack));
        Assert.That(_inventory.TryRemove(_potion, 9), Is.EqualTo(ItemError.Insufficient));
        Assert.That(_inventory.GetQuantity(_potion), Is.EqualTo(8));
    }

    // ---- 판정 내부 흡수: 비스택형 ----

    [Test]
    public void Unstackable_grant_creates_quantity_one_instances()
    {
        var created = new List<ItemInstance<ItemState>>();

        Assert.That(_inventory.TryAdd(_sword, 2, created), Is.EqualTo(ItemError.None));
        Assert.That(created, Has.Count.EqualTo(2));
        Assert.That(created[0].Quantity, Is.EqualTo(1));
        Assert.That(created[0].InstanceId, Is.Not.EqualTo(created[1].InstanceId));
        Assert.That(_inventory.GetQuantity(_sword), Is.EqualTo(2));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(2));
    }

    [Test]
    public void Unstackable_max_stack_bounds_held_instances()
    {
        _inventory.TryAdd(_sword, 3);

        Assert.That(_inventory.TryAdd(_sword, 1), Is.EqualTo(ItemError.ExceedsMaxStack));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(3));
    }

    [Test]
    public void Unstackable_bulk_grant_is_capped_per_operation()
    {
        Assert.That(_inventory.TryAdd(_relic, 1001), Is.EqualTo(ItemError.InvalidAmount));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(0));
    }

    // ---- 잠금 ----

    [Test]
    public void Locked_instances_are_excluded_from_removal()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_sword, 2, created);
        created[0].Flags = FlagInUse;

        Assert.That(_inventory.TryRemove(_sword, 2), Is.EqualTo(ItemError.Insufficient));
        Assert.That(_inventory.TryRemove(_sword, 1), Is.EqualTo(ItemError.None));
        Assert.That(_inventory.TryGetInstance(created[0].InstanceId, out _), Is.True,
            "잠긴 인스턴스가 남아야 한다");
        Assert.That(_inventory.TryRemoveByInstance(created[0].InstanceId, 1), Is.EqualTo(ItemError.Locked));
    }

    [Test]
    public void Locked_stack_singleton_blocks_consumption_but_not_grant()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_gold, 100, created);
        created[0].Flags = FlagUserLocked;

        Assert.That(_inventory.TryRemove(_gold, 1), Is.EqualTo(ItemError.Insufficient));
        Assert.That(_inventory.TryAdd(_gold, 50), Is.EqualTo(ItemError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo(150));
    }

    [Test]
    public void RemoveByInstance_consumes_partial_stack_or_whole_instance()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_gold, 100, created);
        _inventory.TryAdd(_sword, 1, created);

        Assert.That(_inventory.TryRemoveByInstance(created[0].InstanceId, 40), Is.EqualTo(ItemError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo(60));
        Assert.That(_inventory.TryRemoveByInstance(created[1].InstanceId, 1), Is.EqualTo(ItemError.None));
        Assert.That(_inventory.GetQuantity(_sword), Is.EqualTo(0));
        Assert.That(_inventory.TryRemoveByInstance(9999, 1), Is.EqualTo(ItemError.UnknownInstance));
    }

    // ---- 트랜잭션 ----

    [Test]
    public void Apply_mixes_stack_and_instance_deltas_atomically()
    {
        _inventory.TryAdd(_gold, 500);
        var created = new List<ItemInstance<ItemState>>();

        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[3];
        deltas[0] = new ItemDelta<long>(_gold, -300);   // 재화 소모
        deltas[1] = new ItemDelta<long>(_sword, 1);     // 장비 지급
        deltas[2] = new ItemDelta<long>(_potion, 5);    // 소모품 지급

        Assert.That(_inventory.TryApply(deltas, out _, created), Is.EqualTo(ItemError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo(200));
        Assert.That(_inventory.GetQuantity(_sword), Is.EqualTo(1));
        Assert.That(_inventory.GetQuantity(_potion), Is.EqualTo(5));
        Assert.That(created, Has.Count.EqualTo(2));   // 검 인스턴스 + 물약 싱글턴
    }

    [Test]
    public void Apply_failure_leaves_inventory_untouched_and_skips_id_issuer()
    {
        _inventory.TryAdd(_gold, 100);
        var idsBefore = _nextId;

        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[3];
        deltas[0] = new ItemDelta<long>(_sword, 2);
        deltas[1] = new ItemDelta<long>(_gold, -50);
        deltas[2] = new ItemDelta<long>(_gold, -60);    // 잔량 50 < 60

        Assert.That(_inventory.TryApply(deltas, out var failedIndex), Is.EqualTo(ItemError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(2));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo(100));
        Assert.That(_inventory.GetQuantity(_sword), Is.EqualTo(0));
        Assert.That(_nextId, Is.EqualTo(idsBefore), "실패한 배치는 id 발급자를 호출하면 안 된다");
    }

    [Test]
    public void Apply_accumulates_duplicate_unstackable_deltas()
    {
        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[2];
        deltas[0] = new ItemDelta<long>(_sword, 2);
        deltas[1] = new ItemDelta<long>(_sword, 2);   // 누적 4 > 최대 3

        Assert.That(_inventory.TryApply(deltas, out var failedIndex), Is.EqualTo(ItemError.ExceedsMaxStack));
        Assert.That(failedIndex, Is.EqualTo(1));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(0));
    }

    // ---- 변경 추적 ----

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

        // 갱신 + 제거
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
        Assert.That(buffer, Is.Empty, "저장된 적 없는 인스턴스는 DELETE가 나가면 안 된다");
    }

    [Test]
    public void Flags_setter_and_mark_changed_notify_on_changed()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_sword, 1, created);
        var baseline = _changed;

        created[0].Flags = FlagInUse;    // +1
        created[0].Flags = FlagInUse;    // 동일값 — 통지 없음
        created[0].MarkChanged();        // +1

        Assert.That(_changed, Is.EqualTo(baseline + 2));
    }

    // ---- 로드 ----

    [Test]
    public void Load_accepts_external_ids_without_tracking()
    {
        Assert.That(_inventory.TryLoadInstance(777, _gold, 500, 0, new ItemState()), Is.EqualTo(ItemError.None));
        Assert.That(_inventory.TryLoadInstance(778, _sword, 1, FlagInUse, new ItemState()), Is.EqualTo(ItemError.None));

        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo(500));
        Assert.That(_inventory.HasChanges, Is.False);
        Assert.That(_changed, Is.EqualTo(0));

        // 로드된 스택 싱글턴에 이어서 병합된다
        Assert.That(_inventory.TryAdd(_gold, 1), Is.EqualTo(ItemError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo(501));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(2));
    }

    [Test]
    public void Load_rejects_duplicates_and_invalid_quantities()
    {
        _inventory.TryLoadInstance(1, _gold, 10, 0, new ItemState());

        Assert.That(_inventory.TryLoadInstance(1, _sword, 1, 0, new ItemState()),
            Is.EqualTo(ItemError.DuplicateInstance));
        Assert.That(_inventory.TryLoadInstance(2, _gold, 10, 0, new ItemState()),
            Is.EqualTo(ItemError.DuplicateInstance), "스택형 정의의 두 번째 인스턴스");
        Assert.That(_inventory.TryLoadInstance(3, _sword, 2, 0, new ItemState()),
            Is.EqualTo(ItemError.InvalidAmount), "비스택형은 수량 1만");
        Assert.That(_inventory.TryLoadInstance(4, _potion, 11, 0, new ItemState()),
            Is.EqualTo(ItemError.ExceedsMaxStack));
    }

    // ---- 스택 컨테이너 계약 강제 ----

    [Test]
    public void Stack_container_rejects_unstackable_definitions()
    {
        var container = new ItemStackContainer(_catalog);

        Assert.That(container.TryAdd(_sword, 1), Is.EqualTo(ItemError.NotStackable));
        Assert.That(container.TryRemove(_sword, 1), Is.EqualTo(ItemError.NotStackable));
        Assert.That(container.TryLoad(_sword, 1), Is.EqualTo(ItemError.NotStackable));

        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[1];
        deltas[0] = new ItemDelta<long>(_sword, 1);
        Assert.That(container.TryApply(deltas, out var failedIndex), Is.EqualTo(ItemError.NotStackable));
        Assert.That(failedIndex, Is.EqualTo(0));
    }

    // ---- 무할당 ----

    [Test]
    public void Query_and_stack_paths_do_not_allocate()
    {
        _inventory.TryAdd(_gold, 1000);
        _inventory.TryAdd(_sword, 2);
        Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[2];
        deltas[0] = new ItemDelta<long>(_gold, 10);
        deltas[1] = new ItemDelta<long>(_gold, -10);

        for (var i = 0; i < 3; i++)   // 워밍업 (열거 포함 — Dictionary.Values 지연 할당 1회 소화)
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

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
    }
}
