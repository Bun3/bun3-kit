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

        // t=35: 25 elapsed / period 10 -> 2 units, basis advances only by consumed periods (30)
        Assert.That(Regen.SettlePeriodic(0, 100, 10, 35, ref refresh), Is.EqualTo(2));
        Assert.That(refresh, Is.EqualTo(30));

        // t=44: 14 elapsed -> 1 unit, basis 40 — cumulative calls match (44-10)/10 = 3 (no drift)
        Assert.That(Regen.SettlePeriodic(2, 100, 10, 44, ref refresh), Is.EqualTo(1));
        Assert.That(refresh, Is.EqualTo(40));

        // t=49: below one period -> 0 units, basis kept
        Assert.That(Regen.SettlePeriodic(3, 100, 10, 49, ref refresh), Is.EqualTo(0));
        Assert.That(refresh, Is.EqualTo(40));
    }

    [Test]
    public void Clamps_at_max_and_resets_bank_when_full()
    {
        long refresh = 0;
        Regen.SettlePeriodic(0, 5, 10, 100, ref refresh);   // initialize basis

        // 1000 elapsed -> worth 90 units but cap is 5 — on reaching full, basis resets to now (no banking)
        Assert.That(Regen.SettlePeriodic(0, 5, 10, 1100, ref refresh), Is.EqualTo(5));
        Assert.That(refresh, Is.EqualTo(1100));

        // already full -> 0 units, basis resets to now (no banking while full)
        Assert.That(Regen.SettlePeriodic(5, 5, 10, 1250, ref refresh), Is.EqualTo(0));
        Assert.That(refresh, Is.EqualTo(1250));

        // resume after spending — reaching full again via regen resets basis to now
        Assert.That(Regen.SettlePeriodic(4, 5, 10, 1265, ref refresh), Is.EqualTo(1));
        Assert.That(refresh, Is.EqualTo(1265), "basis reset on reaching full");
    }

    // ---- Inventory auto-settlement ----

    private sealed class ItemState;

    [Test]
    public void SettleRegen_charges_all_regen_definitions_from_catalog_meta()
    {
        var catalog = new ItemCatalogBuilder<string>()
            .Register("ticket", "Ticket", regenPeriodTicks: 10, maxRegen: 5)
            .Register("energy", "Energy", regenPeriodTicks: 100, maxRegen: 100)
            .Register("gold", "Gold")
            .Build();
        var ticket = catalog.GetRequired("ticket");
        var energy = catalog.GetRequired("energy");
        long nextId = 0;
        var applied = 0;
        var inventory = new ItemInventory<ItemState>(
            catalog, () => ++nextId, _ => new ItemState(), onApplied: _ => applied++);

        Assert.That(inventory.SettleRegen(1000), Is.EqualTo(0), "uninitialized — only sets basis");
        Assert.That(inventory.SettleRegen(1035), Is.EqualTo(1), "3 tickets (period 10), energy below one period");
        Assert.That(inventory.GetQuantity(ticket), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)3));
        Assert.That(applied, Is.EqualTo(1), "regen batch commits atomically once");

        // regen continues even with 0 instances (all spent) — basis lives in a per-definition map
        inventory.TryRemove(ticket, 3);
        Assert.That(inventory.InstanceCount, Is.EqualTo(0));
        Assert.That(inventory.SettleRegen(1100), Is.EqualTo(2), "ticket recharge + 1 energy");
        Assert.That(inventory.GetQuantity(ticket), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)5), "70 elapsed since 1030 -> 7 units, clamped to cap 5");
        Assert.That(inventory.GetQuantity(energy), Is.EqualTo(Bun3.Gameplay.Numerics.BigNum.One));
    }

    [Test]
    public void Regen_target_and_hard_cap_are_separate_knobs()
    {
        // Reward grants stack past the regen target (5) up to maxCount (default unlimited);
        // regen fills only while the total is below the target.
        var catalog = new ItemCatalogBuilder<string>()
            .Register("ticket", "Dungeon Ticket", regenPeriodTicks: 10, maxRegen: 5)
            .Register("strict", "Strict Ticket", maxCount: 5, regenPeriodTicks: 10, maxRegen: 5)
            .Build();
        var ticket = catalog.GetRequired("ticket");
        var strict = catalog.GetRequired("strict");
        Assert.That(catalog.GetMaxCount(ticket), Is.EqualTo(long.MaxValue), "default maxCount is unlimited");
        Assert.That(catalog.GetMaxRegen(ticket), Is.EqualTo(5));
        long nextId = 0;
        var inventory = new ItemInventory<ItemState>(catalog, () => ++nextId, _ => new ItemState());

        inventory.SettleRegen(100);                 // initialize basis
        inventory.SettleRegen(160);                 // regen 5 (reaches target)
        Assert.That(inventory.TryAdd(ticket, 7), Is.EqualTo(InventoryError.None), "rewards stack past target");
        Assert.That(inventory.GetQuantity(ticket), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)12));

        Assert.That(inventory.SettleRegen(500), Is.EqualTo(0), "at/above target — regen stops, no banking");

        inventory.TryRemove(ticket, 9);             // total 3 < target — regen resumes
        Assert.That(inventory.SettleRegen(530), Is.EqualTo(1), "30 elapsed since 500 -> 2 tickets");
        Assert.That(inventory.GetQuantity(ticket), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)5));

        // clamped grants use maxCount — unlimited means full amount
        Assert.That(inventory.TryAddUpTo(ticket, 100, out var granted), Is.EqualTo(InventoryError.None));
        Assert.That(granted, Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)100));

        // strict definition (maxCount == maxRegen) hits the hard cap on grants past the target
        Assert.That(inventory.TryAdd(strict, 6), Is.EqualTo(InventoryError.ExceedsMaxCount));

        // loading a quantity above the target is fine as long as it is within maxCount
        var reloaded = new ItemInventory<ItemState>(catalog, () => ++nextId, _ => new ItemState());
        Assert.That(reloaded.TryLoadInstance(1, ticket, 12, 0, new ItemState()), Is.EqualTo(InventoryError.None));
    }

    [Test]
    public void SettleRegen_basis_persists_via_load_roundtrip()
    {
        var catalog = new ItemCatalogBuilder<string>()
            .Register("ticket", "Ticket", regenPeriodTicks: 10, maxRegen: 5)
            .Build();
        var ticket = catalog.GetRequired("ticket");
        long nextId = 0;
        var inventory = new ItemInventory<ItemState>(catalog, () => ++nextId, _ => new ItemState());

        inventory.SettleRegen(1000);
        inventory.SettleRegen(1025);   // 2 units, basis 1020
        Assert.That(inventory.GetRegenBasis(ticket), Is.EqualTo(1020));

        // save->load replay: restore basis into a fresh inventory and keep settling
        var reloaded = new ItemInventory<ItemState>(catalog, () => ++nextId, _ => new ItemState());
        reloaded.TryLoadInstance(99, ticket, 2, 0, new ItemState());
        reloaded.LoadRegenBasis(ticket, 1020);
        Assert.That(reloaded.SettleRegen(1044), Is.EqualTo(1), "24 elapsed -> 2 units");
        Assert.That(reloaded.GetQuantity(ticket), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)4));
    }

    [Test]
    public void Regen_definitions_enforce_integer_quantities_and_valid_meta()
    {
        Assert.That(() => new ItemCatalogBuilder<string>()
                .Register("x", "x", maxCount: 5, unstackable: true, regenPeriodTicks: 10, maxRegen: 5),
            Throws.TypeOf<ItemCatalogException>(), "unstackable regen unsupported");
        Assert.That(() => new ItemCatalogBuilder<string>()
                .Register("x", "x", regenPeriodTicks: 10),
            Throws.TypeOf<ItemCatalogException>(), "regen definition requires maxRegen");
        Assert.That(() => new ItemCatalogBuilder<string>()
                .Register("x", "x", maxRegen: 5),
            Throws.TypeOf<ItemCatalogException>(), "maxRegen is meaningless without a regen period");
        Assert.That(() => new ItemCatalogBuilder<string>()
                .Register("x", "x", maxCount: 3, regenPeriodTicks: 10, maxRegen: 5),
            Throws.TypeOf<ItemCatalogException>(), "maxRegen > maxCount forbidden");

        var catalog = new ItemCatalogBuilder<string>()
            .Register("ticket", "Ticket", regenPeriodTicks: 10, maxRegen: 5)
            .Build();
        var ticket = catalog.GetRequired("ticket");
        long nextId = 0;
        var inventory = new ItemInventory<ItemState>(catalog, () => ++nextId, _ => new ItemState());

        Assert.That(inventory.TryAdd(ticket, (Bun3.Gameplay.Numerics.BigNum)0.5),
            Is.EqualTo(InventoryError.InvalidAmount), "regen definitions are integer-only");
        Assert.That(inventory.TryLoadInstance(1, ticket, (Bun3.Gameplay.Numerics.BigNum)1.5, 0, new ItemState()),
            Is.EqualTo(InventoryError.InvalidAmount));
    }

    [Test]
    public void Guards_uninitialized_and_backwards_clock()
    {
        long refresh = 0;
        Assert.That(Regen.SettlePeriodic(0, 100, 10, 500, ref refresh), Is.EqualTo(0), "uninitialized initializes basis instead of granting full");
        Assert.That(refresh, Is.EqualTo(500));

        refresh = 900;
        Assert.That(Regen.SettlePeriodic(0, 100, 10, 800, ref refresh), Is.EqualTo(0), "backwards clock guard");
        Assert.That(refresh, Is.EqualTo(800));

        Assert.That(() => Regen.SettlePeriodic(0, 100, 0, 800, ref refresh),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
