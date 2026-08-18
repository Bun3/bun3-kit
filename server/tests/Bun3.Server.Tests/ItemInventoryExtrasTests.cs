using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

/// <summary>v0.4 추가분 — 클램프 지급·만료·수집·적용 통지.</summary>
[TestFixture]
public class ItemInventoryExtrasTests
{
    private sealed class ItemState;

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;      // 스택형, 무제한
    private ItemId _potion;    // 스택형, maxStack 10
    private ItemId _sword;     // 비스택형, 최대 3개
    private long _nextId;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "골드")
            .Register("potion", "물약", maxStack: 10)
            .Register("sword", "검", maxStack: 3, unstackable: true)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _potion = _catalog.GetRequired("potion");
        _sword = _catalog.GetRequired("sword");
        _nextId = 0;
    }

    private ItemInventory<ItemState> NewInventory(InventoryAppliedHandler? onApplied = null) =>
        new(_catalog, () => ++_nextId, _ => new ItemState(), onApplied: onApplied);

    // ---- TryAddUpTo ----

    [Test]
    public void AddUpTo_clamps_to_remaining_capacity_and_reports_granted()
    {
        var inventory = NewInventory();
        inventory.TryAdd(_potion, 7);

        Assert.That(inventory.TryAddUpTo(_potion, 5, out var granted), Is.EqualTo(InventoryError.None));
        Assert.That(granted, Is.EqualTo((BigNum)3), "상한 10 - 보유 7");
        Assert.That(inventory.GetQuantity(_potion), Is.EqualTo((BigNum)10));

        // 가득 — 0 지급 성공, 변경 없음
        Assert.That(inventory.TryAddUpTo(_potion, 5, out granted), Is.EqualTo(InventoryError.None));
        Assert.That(granted, Is.EqualTo(BigNum.Zero));
        Assert.That(inventory.HasChanges, Is.True);   // 앞선 지급분만

        // 무제한은 전량
        Assert.That(inventory.TryAddUpTo(_gold, 100, out granted), Is.EqualTo(InventoryError.None));
        Assert.That(granted, Is.EqualTo((BigNum)100));
    }

    [Test]
    public void AddUpTo_handles_unstackable_capacity_and_rejects_fractions()
    {
        var inventory = NewInventory();
        var created = new List<ItemInstance<ItemState>>();
        inventory.TryAdd(_sword, 2);

        Assert.That(inventory.TryAddUpTo(_sword, 5, out var granted, created), Is.EqualTo(InventoryError.None));
        Assert.That(granted, Is.EqualTo(BigNum.One), "상한 3 - 보유 2");
        Assert.That(created, Has.Count.EqualTo(1));
        Assert.That(inventory.TryAddUpTo(_sword, (BigNum)0.5, out _), Is.EqualTo(InventoryError.InvalidAmount));
        Assert.That(inventory.TryAddUpTo(_sword, 0, out _), Is.EqualTo(InventoryError.InvalidAmount));
    }

    // ---- 만료 ----

    [Test]
    public void Expiry_field_tracks_changes_and_collects_by_injected_time()
    {
        var inventory = NewInventory();
        var created = new List<ItemInstance<ItemState>>();
        inventory.TryAdd(_sword, 2, created);
        inventory.TryAdd(_gold, 100, created);

        var drain = new List<ItemChange<ItemState>>();
        inventory.DrainChanges(drain);

        created[0].ExpiresAtTicksUtc = 1000;    // setter → Updated 추적
        drain.Clear();
        inventory.DrainChanges(drain);
        Assert.That(drain, Has.Count.EqualTo(1));
        Assert.That(drain[0].Kind, Is.EqualTo(ItemChangeKind.Updated));

        var expired = new List<ItemInstance<ItemState>>();
        Assert.That(inventory.CollectExpired(999, expired), Is.EqualTo(0), "만료 전");
        Assert.That(inventory.CollectExpired(1000, expired), Is.EqualTo(1), "경계 포함(<= now)");
        Assert.That(expired[0], Is.SameAs(created[0]));
        Assert.That(inventory.CollectExpired(9999, expired), Is.EqualTo(1), "0(무기한)은 제외");
    }

    [Test]
    public void Expiry_loads_and_survives_roundtrip()
    {
        var inventory = NewInventory();
        Assert.That(inventory.TryLoadInstance(7, _sword, 1, 0, new ItemState(), expiresAtTicksUtc: 500),
            Is.EqualTo(InventoryError.None));

        Assert.That(inventory.TryGetInstance(7, out var instance), Is.True);
        Assert.That(instance.ExpiresAtTicksUtc, Is.EqualTo(500));
        Assert.That(inventory.HasChanges, Is.False, "로드는 추적 없음");
    }

    // ---- CollectInstances ----

    [Test]
    public void CollectInstances_gathers_by_definition()
    {
        var inventory = NewInventory();
        inventory.TryAdd(_sword, 3);
        inventory.TryAdd(_gold, 100);

        var buffer = new List<ItemInstance<ItemState>>();
        Assert.That(inventory.CollectInstances(_sword, buffer), Is.EqualTo(3));
        Assert.That(inventory.CollectInstances(_gold, buffer), Is.EqualTo(1));
        Assert.That(buffer, Has.Count.EqualTo(4), "이어 담기");
        Assert.That(inventory.CollectInstances(_potion, buffer), Is.EqualTo(0));
    }

    // ---- onApplied ----

    [Test]
    public void OnApplied_reports_net_deltas_once_per_commit()
    {
        var log = new List<(ItemId Item, BigNum Amount)>();
        var commits = 0;
        var inventory = NewInventory(onApplied: applied =>
        {
            commits++;
            foreach (var delta in applied)
            {
                log.Add((delta.Item, delta.Amount));
            }
        });
        var created = new List<ItemInstance<ItemState>>();
        inventory.TryAdd(_sword, 2, created);   // 커밋 1

        var tx = inventory.BeginTransaction();   // 커밋 2 — 혼합 배치
        tx.Remove(_gold, 0);                     // 실패 예정이 아님 — 아래에서 gold 지급부터
        // 다시 구성: 실패 배치는 통지 없음을 먼저 확인
        Assert.That(tx.Commit(out _), Is.EqualTo(InventoryError.InvalidAmount));
        Assert.That(commits, Is.EqualTo(1), "실패 커밋은 통지 없음");

        var tx2 = inventory.BeginTransaction();
        tx2.Add(_gold, 100);
        tx2.RemoveInstance(created[0].InstanceId);
        Assert.That(tx2.Commit(out _), Is.EqualTo(InventoryError.None));

        Assert.That(commits, Is.EqualTo(2));
        Assert.That(log, Has.Count.EqualTo(3));
        Assert.That(log[0], Is.EqualTo((_sword, (BigNum)2)));
        Assert.That(log[1], Is.EqualTo((_gold, (BigNum)100)));
        Assert.That(log[2], Is.EqualTo((_sword, -BigNum.One)), "인스턴스 지정 소모도 순 델타로");
    }
}
