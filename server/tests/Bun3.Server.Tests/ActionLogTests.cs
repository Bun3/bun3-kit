using Bun3.Gameplay.Numerics;
using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

/// <summary>Action log (customer-support audit ledger) — session-wide shared scope tree.</summary>
[TestFixture]
public class ActionLogTests
{
    private sealed class ItemState;

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;
    private ItemId _sword;
    private long _nextId;
    private List<List<ActionLogEntry>> _batches = null!;
    private ActionLog _log = null!;

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
        _batches = new List<List<ActionLogEntry>>();
        _log = new ActionLog(entries => _batches.Add(entries.ToArray().ToList()));
    }

    private ItemInventory<ItemState> NewInventory(string? label = null) =>
        new(_catalog, () => ++_nextId, _ => new ItemState(), log: _log, logLabel: label);

    [Test]
    public void Cross_system_chain_lands_in_one_scope_tree()
    {
        var inventory = NewInventory();
        inventory.TryAdd(_gold, 1000);
        _batches.Clear();

        // chain starting and passing outside the inventory: acquire -> (event) achievement clear -> reward
        using (_log.BeginScope("BuyItem product=1021 x1"))
        {
            inventory.TryRemove(_gold, 500);                  // inventory — auto-attached
            inventory.TryAdd(_sword, 1);                      // acquire — assume a game event fires
            using (_log.BeginScope("Achievement first_sword"))  // achievement system joins the same log
            {
                _log.Log("cleared: first_sword");             // note from a non-inventory system
                inventory.TryAdd(_gold, 100);                 // achievement reward — auto-attached again
            }
        }

        Assert.That(_batches, Has.Count.EqualTo(1), "one batch on root close — the whole chain is one tree");
        var entries = _batches[0];
        Assert.That(entries.Select(e => (e.Kind, e.Depth)), Is.EqualTo(new[]
        {
            (ActionLogEntryKind.ScopeStart, 0),   // BuyItem
            (ActionLogEntryKind.Data, 1),       // gold -500
            (ActionLogEntryKind.Data, 1),       // sword +1
            (ActionLogEntryKind.ScopeStart, 1),   // Achievement
            (ActionLogEntryKind.Note, 2),         // cleared
            (ActionLogEntryKind.Data, 2),       // gold +100 (reward)
        }));
        Assert.That(((InventoryChange)entries[1].Data!).Balance, Is.EqualTo((BigNum)500), "balance after change — the CS answer");
        Assert.That(((InventoryChange)entries[5].Data!).Balance, Is.EqualTo((BigNum)600));
    }

    [Test]
    public void Multiple_inventories_share_one_log_with_labels()
    {
        var bag = NewInventory("bag");
        var warehouse = NewInventory("warehouse");
        bag.TryAdd(_gold, 100);
        _batches.Clear();

        using (_log.BeginScope("Deposit"))
        {
            bag.TryTransfer(warehouse, _gold, 40);
        }

        var entries = _batches.Single();
        Assert.That(entries.Select(e => (e.Kind, e.Source)), Is.EqualTo(new[]
        {
            (ActionLogEntryKind.ScopeStart, (string?)null),
            (ActionLogEntryKind.Data, "bag"),         // -40
            (ActionLogEntryKind.Data, "warehouse"),   // +40
        }));
        Assert.That(((InventoryChange)entries[1].Data!).Delta, Is.EqualTo(-(BigNum)40));
        Assert.That(((InventoryChange)entries[2].Data!).Balance, Is.EqualTo((BigNum)40));
    }

    [Test]
    public void Unscoped_entries_flush_immediately_so_ledger_has_no_gaps()
    {
        var inventory = NewInventory();
        inventory.TryAdd(_gold, 100);   // change outside any scope
        _log.Log("standalone note");    // note outside any scope

        Assert.That(_batches, Has.Count.EqualTo(2));
        Assert.That(_batches[0].Single().Kind, Is.EqualTo(ActionLogEntryKind.Data));
        Assert.That(_batches[1].Single().Kind, Is.EqualTo(ActionLogEntryKind.Note));
    }

    [Test]
    public void Failed_commits_record_nothing_and_scopes_close_in_order()
    {
        var inventory = NewInventory();

        using (var outer = _log.BeginScope("action"))
        {
            Assert.That(inventory.TryRemove(_gold, 5), Is.EqualTo(InventoryError.Insufficient));

            var inner = _log.BeginScope("inner");
            Assert.That(() => outer.Dispose(), Throws.InvalidOperationException, "reverse-order close is enforced");
            inner.Dispose();
        }

        Assert.That(_batches, Has.Count.EqualTo(1));
        Assert.That(_batches[0].All(e => e.Kind == ActionLogEntryKind.ScopeStart), Is.True,
            "failed commits record no Change");
    }

    [Test]
    public void Inventory_without_log_reference_is_unaffected()
    {
        var silent = new ItemInventory<ItemState>(_catalog, () => ++_nextId, _ => new ItemState());

        using (_log.BeginScope("x"))
        {
            Assert.That(silent.TryAdd(_gold, 1), Is.EqualTo(InventoryError.None));
        }

        Assert.That(_batches.Single().Single().Kind, Is.EqualTo(ActionLogEntryKind.ScopeStart),
            "an inventory without a log reference records nothing");
    }
}
