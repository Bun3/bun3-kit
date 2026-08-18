using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

/// <summary>인벤토리 간 이동 — 같은 플레이어의 가방↔창고 (유저간 거래 아님).</summary>
[TestFixture]
public class ItemTransferTests
{
    private sealed class ItemState
    {
        public int Level = 1;
    }

    private const uint FlagLocked = 1u << 0;

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;      // 스택형, 무제한
    private ItemId _potion;    // 스택형, maxCount 10
    private ItemId _sword;     // 비스택형, 최대 3개
    private long _nextId;
    private ItemInventory<ItemState> _bag = null!;
    private ItemInventory<ItemState> _warehouse = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "골드")
            .Register("potion", "물약", maxCount: 10)
            .Register("sword", "검", maxCount: 3, unstackable: true)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _potion = _catalog.GetRequired("potion");
        _sword = _catalog.GetRequired("sword");
        _nextId = 0;
        _bag = NewInventory();
        _warehouse = NewInventory();
    }

    private ItemInventory<ItemState> NewInventory() =>
        new(_catalog, () => ++_nextId, _ => new ItemState(), removeBlockingFlags: FlagLocked);

    [Test]
    public void Unstackable_transfer_preserves_instance_identity_and_state()
    {
        var created = new List<ItemInstance<ItemState>>();
        _bag.TryAdd(_sword, 2, created);
        created[0].State.Level = 7;
        var movedId = created[0].InstanceId;
        _bag.DrainChanges(new List<ItemChange<ItemState>>());   // 저장 시뮬레이션 — INSERT 완료 상태

        Assert.That(_bag.TryTransferByInstance(_warehouse, movedId), Is.EqualTo(InventoryError.None));
        Assert.That(_bag.GetQuantity(_sword), Is.EqualTo(BigNum.One));
        Assert.That(_warehouse.TryGetInstance(movedId, out var moved), Is.True, "id 보존");
        Assert.That(moved.State.Level, Is.EqualTo(7), "상태 보존");

        // 변경 추적: 출발 Removed, 도착 Created — DB에선 소유 컬럼 UPDATE로 매핑 가능
        var bagChanges = new List<ItemChange<ItemState>>();
        var whChanges = new List<ItemChange<ItemState>>();
        _bag.DrainChanges(bagChanges);
        _warehouse.DrainChanges(whChanges);
        Assert.That(bagChanges.Any(c => c.Kind == ItemChangeKind.Removed && c.InstanceId == movedId), Is.True);
        Assert.That(whChanges.Any(c => c.Kind == ItemChangeKind.Created && c.InstanceId == movedId), Is.True);
    }

    [Test]
    public void Stackable_transfer_merges_quantities_and_respects_target_cap()
    {
        _bag.TryAdd(_potion, 8);
        _warehouse.TryAdd(_potion, 5);

        Assert.That(_bag.TryTransfer(_warehouse, _potion, 6), Is.EqualTo(InventoryError.ExceedsMaxCount), "5+6 > 10");
        Assert.That(_bag.TryTransfer(_warehouse, _potion, 5), Is.EqualTo(InventoryError.None));
        Assert.That(_bag.GetQuantity(_potion), Is.EqualTo((BigNum)3));
        Assert.That(_warehouse.GetQuantity(_potion), Is.EqualTo((BigNum)10));
        Assert.That(_warehouse.InstanceCount, Is.EqualTo(1), "싱글턴 병합");
    }

    [Test]
    public void Transfer_failures_leave_both_untouched_and_locks_block()
    {
        var created = new List<ItemInstance<ItemState>>();
        _bag.TryAdd(_sword, 2, created);
        _bag.TryAdd(_gold, 100, created);
        created[0].Flags = FlagLocked;

        Assert.That(_bag.TryTransfer(_warehouse, _sword, 2), Is.EqualTo(InventoryError.Insufficient), "잠금 제외 1개뿐");
        Assert.That(_bag.TryTransferByInstance(_warehouse, created[0].InstanceId), Is.EqualTo(InventoryError.Locked));
        Assert.That(_bag.TryTransfer(_warehouse, _gold, 200), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(_bag.GetQuantity(_sword), Is.EqualTo((BigNum)2));
        Assert.That(_warehouse.InstanceCount, Is.EqualTo(0));

        Assert.That(() => _bag.TryTransfer(_bag, _gold, 1), Throws.ArgumentException, "자기 자신 금지");
        var foreign = new ItemCatalogBuilder<string>().Register("gold", "골드").Build();
        var other = new ItemInventory<ItemState>(foreign, () => ++_nextId, _ => new ItemState());
        Assert.That(() => _bag.TryTransfer(other, _gold, 1), Throws.ArgumentException, "카탈로그 불일치 금지");
    }

    [Test]
    public void Stack_singleton_instance_transfer_moves_full_quantity()
    {
        var created = new List<ItemInstance<ItemState>>();
        _bag.TryAdd(_gold, 100, created);

        Assert.That(_bag.TryTransferByInstance(_warehouse, created[0].InstanceId), Is.EqualTo(InventoryError.None));
        Assert.That(_bag.GetQuantity(_gold), Is.EqualTo(BigNum.Zero));
        Assert.That(_warehouse.GetQuantity(_gold), Is.EqualTo((BigNum)100));
    }

    [Test]
    public void Transfer_notifies_both_sides_with_balances()
    {
        var log = new List<(string Side, BigNum Delta, BigNum Balance)>();
        var source = new ItemInventory<ItemState>(_catalog, () => ++_nextId, _ => new ItemState(),
            onApplied: applied => log.Add(("src", applied[0].Delta, applied[0].Balance)));
        var target = new ItemInventory<ItemState>(_catalog, () => ++_nextId, _ => new ItemState(),
            onApplied: applied => log.Add(("dst", applied[0].Delta, applied[0].Balance)));
        source.TryAdd(_gold, 100);
        log.Clear();

        Assert.That(source.TryTransfer(target, _gold, 30), Is.EqualTo(InventoryError.None));
        Assert.That(log, Has.Count.EqualTo(2));
        Assert.That(log[0], Is.EqualTo(("src", -(BigNum)30, (BigNum)70)));
        Assert.That(log[1], Is.EqualTo(("dst", (BigNum)30, (BigNum)30)));
    }
}
