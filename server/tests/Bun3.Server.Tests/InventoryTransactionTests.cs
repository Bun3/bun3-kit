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
    private ItemId _gold;      // 스택형
    private ItemId _sword;     // 비스택형
    private long _nextId;
    private ItemInventory<ItemState> _inventory = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "골드")
            .Register("sword", "검", unstackable: true)
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
        _inventory.TryAdd(_sword, 2, created);   // 검 #1, #2
        _inventory.TryAdd(_gold, 100);

        // 특정 검 파괴 + 골드 소모 + 새 검 지급 — TryApply로는 불가능한 배치
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
        _inventory.TryAdd(_sword, 2, created);   // 검 2자루

        // 정의 단위 소모(1)가 지정 인스턴스를 집으면 지정 소모가 깨진다 — 풀에서 제외돼야 함.
        // 순서를 정의 소모 → 인스턴스 지정으로 놓아 전역 제외 규칙을 검증한다.
        var tx = _inventory.BeginTransaction();
        tx.Remove(_sword, 1);
        tx.RemoveInstance(created[0].InstanceId);

        Assert.That(tx.Commit(out _), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(0));

        // 검 1자루뿐인데 지정 + 정의 소모를 동시에 요구하면 부족이 맞다
        _inventory.TryAdd(_sword, 1, created);
        var tx2 = _inventory.BeginTransaction();
        tx2.RemoveInstance(created[^1].InstanceId);
        tx2.Remove(_sword, 1);
        Assert.That(tx2.Commit(out var failedIndex), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(1));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(1), "실패한 배치는 무변경");
    }

    [Test]
    public void Singleton_remove_instance_settles_against_definition_pool()
    {
        var created = new List<ItemInstance<ItemState>>();
        _inventory.TryAdd(_gold, 100, created);
        var goldId = created[0].InstanceId;

        // 부분 소모 + 정의 소모가 같은 풀에서 정산된다
        var tx = _inventory.BeginTransaction();
        tx.RemoveInstance(goldId, 60);
        tx.Remove(_gold, 50);   // 잔량 40 < 50
        Assert.That(tx.Commit(out var failedIndex), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(1));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)100));

        // 전량 지정(RemoveInstanceAll)은 잔량 전부를 정산한다
        var tx2 = _inventory.BeginTransaction();
        tx2.Remove(_gold, 40);
        tx2.RemoveInstance(goldId);   // 남은 60 전부
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
        tx.RemoveInstance(created[0].InstanceId);   // 같은 인스턴스 중복 지정
        Assert.That(tx.Commit(out var failedIndex), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(1));

        created[1].Flags = FlagLocked;
        var tx2 = _inventory.BeginTransaction();
        tx2.RemoveInstance(created[1].InstanceId);
        Assert.That(tx2.Commit(out _), Is.EqualTo(InventoryError.Locked));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(2), "실패한 배치는 무변경");
    }

    [Test]
    public void Stale_or_committed_builders_are_rejected()
    {
        var tx = _inventory.BeginTransaction();
        tx.Add(_gold, 1);

        var tx2 = _inventory.BeginTransaction();   // 이전 배치 폐기
        Assert.That(() => tx.Add(_gold, 1), Throws.InvalidOperationException);

        tx2.Add(_gold, 5);
        Assert.That(tx2.Commit(out _), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)5), "폐기된 배치의 연산은 반영 안 됨");
        Assert.That(() => tx2.Commit(out _), Throws.InvalidOperationException, "커밋 후 재사용 금지");
    }

    [Test]
    public void Commit_does_not_allocate_without_instance_creation()
    {
        _inventory.TryAdd(_gold, 1_000_000);

        for (var i = 0; i < 3; i++)   // 워밍업
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
