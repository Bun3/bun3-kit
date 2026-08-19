using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

/// <summary>v0.4 additions — clamped grant, expiry, collection, applied notification.</summary>
[TestFixture]
public class ItemInventoryExtrasTests
{
    private sealed class ItemState;

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;      // stackable, unbounded
    private ItemId _potion;    // stackable, maxCount 10
    private ItemId _sword;     // unstackable, max 3
    private long _nextId;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "Gold")
            .Register("potion", "Potion", maxCount: 10)
            .Register("sword", "Sword", maxCount: 3, unstackable: true)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _potion = _catalog.GetRequired("potion");
        _sword = _catalog.GetRequired("sword");
        _nextId = 0;
    }

    private ItemInventory<ItemState> NewInventory(InventoryAppliedHandler? onApplied = null) =>
        new(_catalog, () => ++_nextId, _ => new ItemState(),
            removeBlockingFlags: 1u << 0, onApplied: onApplied);

    // ---- TryAddUpTo ----

    [Test]
    public void AddUpTo_clamps_to_remaining_capacity_and_reports_granted()
    {
        var inventory = NewInventory();
        inventory.TryAdd(_potion, 7);

        Assert.That(inventory.TryAddUpTo(_potion, 5, out var granted), Is.EqualTo(InventoryError.None));
        Assert.That(granted, Is.EqualTo((BigNum)3), "cap 10 - held 7");
        Assert.That(inventory.GetQuantity(_potion), Is.EqualTo((BigNum)10));

        // Full — zero-grant success, no change
        Assert.That(inventory.TryAddUpTo(_potion, 5, out granted), Is.EqualTo(InventoryError.None));
        Assert.That(granted, Is.EqualTo(BigNum.Zero));
        Assert.That(inventory.HasChanges, Is.True);   // only the earlier grant

        // Unbounded grants in full
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
        Assert.That(granted, Is.EqualTo(BigNum.One), "cap 3 - held 2");
        Assert.That(created, Has.Count.EqualTo(1));
        Assert.That(inventory.TryAddUpTo(_sword, (BigNum)0.5, out _), Is.EqualTo(InventoryError.InvalidAmount));
        Assert.That(inventory.TryAddUpTo(_sword, 0, out _), Is.EqualTo(InventoryError.InvalidAmount));
    }

    // ---- Expiry ----

    [Test]
    public void Expiry_field_tracks_changes_and_collects_by_injected_time()
    {
        var inventory = NewInventory();
        var created = new List<ItemInstance<ItemState>>();
        inventory.TryAdd(_sword, 2, created);
        inventory.TryAdd(_gold, 100, created);

        var drain = new List<ItemChange<ItemState>>();
        inventory.DrainChanges(drain);

        created[0].ExpiresAtTicksUtc = 1000;    // setter → tracked as Updated
        drain.Clear();
        inventory.DrainChanges(drain);
        Assert.That(drain, Has.Count.EqualTo(1));
        Assert.That(drain[0].Kind, Is.EqualTo(ItemChangeKind.Updated));

        var expired = new List<ItemInstance<ItemState>>();
        Assert.That(inventory.CollectExpired(999, expired), Is.EqualTo(0), "before expiry");
        Assert.That(inventory.CollectExpired(1000, expired), Is.EqualTo(1), "boundary inclusive (<= now)");
        Assert.That(expired[0], Is.SameAs(created[0]));
        Assert.That(inventory.CollectExpired(9999, expired), Is.EqualTo(1), "0 (no expiry) is excluded");
    }

    [Test]
    public void Expiry_loads_and_survives_roundtrip()
    {
        var inventory = NewInventory();
        Assert.That(inventory.TryLoadInstance(7, _sword, 1, 0, new ItemState(), expiresAtTicksUtc: 500),
            Is.EqualTo(InventoryError.None));

        Assert.That(inventory.TryGetInstance(7, out var instance), Is.True);
        Assert.That(instance.ExpiresAtTicksUtc, Is.EqualTo(500));
        Assert.That(inventory.HasChanges, Is.False, "load is untracked");
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
        Assert.That(buffer, Has.Count.EqualTo(4), "appends to buffer");
        Assert.That(inventory.CollectInstances(_potion, buffer), Is.EqualTo(0));
    }

    // ---- Split pools (regen/bonus) with priority consumption ----

    [Test]
    public void Split_pool_tickets_regen_and_bonus_work_together()
    {
        var catalog = new ItemCatalogBuilder<string>()
            .Register("ticket.regen", "Regen Ticket", regenPeriodTicks: 10, maxRegen: 5)
            .Register("ticket.bonus", "Bonus Ticket")
            .Build();
        var regen = catalog.GetRequired("ticket.regen");
        var bonus = catalog.GetRequired("ticket.bonus");
        long nextId = 0;
        var inventory = new ItemInventory<ItemState>(catalog, () => ++nextId, _ => new ItemState());
        Span<ItemId> pools = stackalloc ItemId[] { bonus, regen };   // bonus consumed first

        inventory.SettleRegen(100);                    // initialize baseline
        inventory.SettleRegen(160);                    // regen 5 (hits cap)
        inventory.TryAdd(bonus, 7);                    // bonus accrues regardless of cap
        Assert.That(inventory.GetQuantityAcross(pools), Is.EqualTo((BigNum)12));

        // Drain bonus (7) first, then 2 from regen — all-or-nothing
        Assert.That(inventory.TryRemoveAcross(pools, 9), Is.EqualTo(InventoryError.None));
        Assert.That(inventory.GetQuantity(bonus), Is.EqualTo(BigNum.Zero));
        Assert.That(inventory.GetQuantity(regen), Is.EqualTo((BigNum)3));

        // Available total (3) insufficient — no change
        Assert.That(inventory.TryRemoveAcross(pools, 4), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(inventory.GetQuantity(regen), Is.EqualTo((BigNum)3));

        // Regen pool dropped below cap, so it refills
        Assert.That(inventory.SettleRegen(190), Is.EqualTo(1), "30 ticks since 160 → 2 tickets (3+2 clamped to cap 5)");
        Assert.That(inventory.GetQuantity(regen), Is.EqualTo((BigNum)5));
    }

    [Test]
    public void RemoveAcross_excludes_locked_and_validates_input()
    {
        var inventory = NewInventory();
        var created = new List<ItemInstance<ItemState>>();
        inventory.TryAdd(_gold, 10, created);
        created[0].Flags = 1u << 0;   // locked — 0 available
        inventory.TryAdd(_potion, 5);
        Span<ItemId> sources = stackalloc ItemId[] { _gold, _potion };

        Assert.That(inventory.GetRemovableQuantity(_gold), Is.EqualTo(BigNum.Zero));
        Assert.That(inventory.TryRemoveAcross(sources, 6), Is.EqualTo(InventoryError.Insufficient), "locked gold excluded");
        Assert.That(inventory.TryRemoveAcross(sources, 5), Is.EqualTo(InventoryError.None), "potion only");
        Assert.That(inventory.GetQuantity(_gold), Is.EqualTo((BigNum)10));
        Assert.That(inventory.TryRemoveAcross(sources, 0), Is.EqualTo(InventoryError.InvalidAmount));
        Span<ItemId> bad = stackalloc ItemId[] { ItemId.None };
        Assert.That(inventory.TryRemoveAcross(bad, 1), Is.EqualTo(InventoryError.UnknownItem));
    }

    // ---- onApplied ----

    [Test]
    public void OnApplied_reports_deltas_and_balances_once_per_commit()
    {
        var log = new List<(ItemId Item, BigNum Delta, BigNum Balance)>();
        var commits = 0;
        var inventory = NewInventory(onApplied: applied =>
        {
            commits++;
            foreach (var change in applied)
            {
                log.Add((change.Item, change.Delta, change.Balance));
            }
        });
        var created = new List<ItemInstance<ItemState>>();
        inventory.TryAdd(_sword, 2, created);   // commit 1

        var tx = inventory.BeginTransaction();
        tx.Remove(_gold, 0);
        Assert.That(tx.Commit(out _), Is.EqualTo(InventoryError.InvalidAmount));
        Assert.That(commits, Is.EqualTo(1), "failed commit does not notify");

        var tx2 = inventory.BeginTransaction();
        tx2.Add(_gold, 100);
        tx2.RemoveInstance(created[0].InstanceId);
        Assert.That(tx2.Commit(out _), Is.EqualTo(InventoryError.None));

        Assert.That(commits, Is.EqualTo(2));
        Assert.That(log, Has.Count.EqualTo(3));
        // (delta, balance after change)
        Assert.That(log[0], Is.EqualTo((_sword, (BigNum)2, (BigNum)2)));
        Assert.That(log[1], Is.EqualTo((_gold, (BigNum)100, (BigNum)100)));
        Assert.That(log[2], Is.EqualTo((_sword, -BigNum.One, BigNum.One)), "targeted instance removal also reports net delta and balance");
    }
}
