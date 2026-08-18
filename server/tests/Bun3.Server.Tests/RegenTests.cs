using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class RegenTests
{
    [Test]
    public void Accrues_whole_periods_and_preserves_remainder_without_drift()
    {
        long refresh = 10;

        // t=35: 25 경과 / 주기 10 → 2개, 기준은 소비한 주기만큼만 전진(30)
        Assert.That(Regen.SettlePeriodic(0, 100, 10, 35, ref refresh), Is.EqualTo(2));
        Assert.That(refresh, Is.EqualTo(30));

        // t=44: 14 경과 → 1개, 기준 40 — 연속 호출 합산이 (44-10)/10 = 3과 일치(드리프트 없음)
        Assert.That(Regen.SettlePeriodic(2, 100, 10, 44, ref refresh), Is.EqualTo(1));
        Assert.That(refresh, Is.EqualTo(40));

        // t=49: 주기 미만 — 0개, 기준 유지
        Assert.That(Regen.SettlePeriodic(3, 100, 10, 49, ref refresh), Is.EqualTo(0));
        Assert.That(refresh, Is.EqualTo(40));
    }

    [Test]
    public void Clamps_at_max_and_resets_bank_when_full()
    {
        long refresh = 0;
        Regen.SettlePeriodic(0, 5, 10, 100, ref refresh);   // 초기화

        // 1000 경과 → 90개분이지만 상한 5 — 가득 도달 시 기준을 현재로(적립 제거)
        Assert.That(Regen.SettlePeriodic(0, 5, 10, 1100, ref refresh), Is.EqualTo(5));
        Assert.That(refresh, Is.EqualTo(1100));

        // 이미 가득 — 0개, 기준 현재로 재설정(가득 상태 적립 방지)
        Assert.That(Regen.SettlePeriodic(5, 5, 10, 1250, ref refresh), Is.EqualTo(0));
        Assert.That(refresh, Is.EqualTo(1250));

        // 소모 후 재개 — 충전으로 다시 가득에 도달하면 기준을 현재로 재설정
        Assert.That(Regen.SettlePeriodic(4, 5, 10, 1265, ref refresh), Is.EqualTo(1));
        Assert.That(refresh, Is.EqualTo(1265), "가득 도달 재설정");
    }

    // ---- 인벤토리 자동 정산 ----

    private sealed class ItemState;

    [Test]
    public void SettleRegen_charges_all_regen_definitions_from_catalog_meta()
    {
        var catalog = new ItemCatalogBuilder<string>()
            .Register("ticket", "티켓", maxStack: 5, regenPeriodTicks: 10)
            .Register("energy", "에너지", maxStack: 100, regenPeriodTicks: 100)
            .Register("gold", "골드")
            .Build();
        var ticket = catalog.GetRequired("ticket");
        var energy = catalog.GetRequired("energy");
        long nextId = 0;
        var applied = 0;
        var inventory = new ItemInventory<ItemState>(
            catalog, () => ++nextId, _ => new ItemState(), onApplied: _ => applied++);

        Assert.That(inventory.SettleRegen(1000), Is.EqualTo(0), "미초기화 — 기준만 초기화");
        Assert.That(inventory.SettleRegen(1035), Is.EqualTo(1), "티켓 3개(주기 10), 에너지는 주기 미달");
        Assert.That(inventory.GetQuantity(ticket), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)3));
        Assert.That(applied, Is.EqualTo(1), "충전 배치는 원자 커밋 1회");

        // 인스턴스 0개(전량 소모) 후에도 리젠 계속 — 기준 시각이 정의별 맵에 있으므로
        inventory.TryRemove(ticket, 3);
        Assert.That(inventory.InstanceCount, Is.EqualTo(0));
        Assert.That(inventory.SettleRegen(1100), Is.EqualTo(2), "티켓 재충전 + 에너지 1개");
        Assert.That(inventory.GetQuantity(ticket), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)5), "1030 기준 70경과 → 7개, 상한 5 클램프");
        Assert.That(inventory.GetQuantity(energy), Is.EqualTo(Bun3.Gameplay.Numerics.BigNum.One));
    }

    [Test]
    public void SettleRegen_basis_persists_via_load_roundtrip()
    {
        var catalog = new ItemCatalogBuilder<string>()
            .Register("ticket", "티켓", maxStack: 5, regenPeriodTicks: 10)
            .Build();
        var ticket = catalog.GetRequired("ticket");
        long nextId = 0;
        var inventory = new ItemInventory<ItemState>(catalog, () => ++nextId, _ => new ItemState());

        inventory.SettleRegen(1000);
        inventory.SettleRegen(1025);   // 2개, 기준 1020
        Assert.That(inventory.GetRegenBasis(ticket), Is.EqualTo(1020));

        // 저장→로드 재현: 새 인벤토리에 기준 복원 후 이어서 정산
        var reloaded = new ItemInventory<ItemState>(catalog, () => ++nextId, _ => new ItemState());
        reloaded.TryLoadInstance(99, ticket, 2, 0, new ItemState());
        reloaded.LoadRegenBasis(ticket, 1020);
        Assert.That(reloaded.SettleRegen(1044), Is.EqualTo(1), "24경과 → 2개");
        Assert.That(reloaded.GetQuantity(ticket), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)4));
    }

    [Test]
    public void Regen_definitions_enforce_integer_quantities_and_valid_meta()
    {
        Assert.That(() => new ItemCatalogBuilder<string>()
                .Register("x", "x", maxStack: 5, unstackable: true, regenPeriodTicks: 10),
            Throws.TypeOf<ItemCatalogException>(), "비스택형 리젠 미지원");
        Assert.That(() => new ItemCatalogBuilder<string>()
                .Register("x", "x", regenPeriodTicks: 10),
            Throws.TypeOf<ItemCatalogException>(), "무제한 maxStack 리젠 금지");

        var catalog = new ItemCatalogBuilder<string>()
            .Register("ticket", "티켓", maxStack: 5, regenPeriodTicks: 10)
            .Build();
        var ticket = catalog.GetRequired("ticket");
        long nextId = 0;
        var inventory = new ItemInventory<ItemState>(catalog, () => ++nextId, _ => new ItemState());

        Assert.That(inventory.TryAdd(ticket, (Bun3.Gameplay.Numerics.BigNum)0.5),
            Is.EqualTo(InventoryError.InvalidAmount), "리젠 정의는 정수만");
        Assert.That(inventory.TryLoadInstance(1, ticket, (Bun3.Gameplay.Numerics.BigNum)1.5, 0, new ItemState()),
            Is.EqualTo(InventoryError.InvalidAmount));
    }

    [Test]
    public void Guards_uninitialized_and_backwards_clock()
    {
        long refresh = 0;
        Assert.That(Regen.SettlePeriodic(0, 100, 10, 500, ref refresh), Is.EqualTo(0), "미초기화는 가득 지급 대신 초기화");
        Assert.That(refresh, Is.EqualTo(500));

        refresh = 900;
        Assert.That(Regen.SettlePeriodic(0, 100, 10, 800, ref refresh), Is.EqualTo(0), "시계 역행 보호");
        Assert.That(refresh, Is.EqualTo(800));

        Assert.That(() => Regen.SettlePeriodic(0, 100, 0, 800, ref refresh),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
