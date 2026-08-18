using Bun3.Gameplay.Numerics;
using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

/// <summary>행동 로그(CS 감사 원장) — 세션 공용 스코프 트리.</summary>
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
            .Register("gold", "골드")
            .Register("sword", "검", unstackable: true)
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

        // 인벤토리 밖에서 시작·경유하는 체인: 획득 → (이벤트) 업적 클리어 → 업적 보상
        using (_log.BeginScope("BuyItem product=1021 x1"))
        {
            inventory.TryRemove(_gold, 500);                  // 인벤토리 — 자동 첨부
            inventory.TryAdd(_sword, 1);                      // 획득 → 게임 이벤트 발화 가정
            using (_log.BeginScope("Achievement first_sword"))  // 업적 시스템 — 같은 로그에 참여
            {
                _log.Log("cleared: first_sword");             // 인벤토리 아닌 시스템의 노트
                inventory.TryAdd(_gold, 100);                 // 업적 보상 — 다시 자동 첨부
            }
        }

        Assert.That(_batches, Has.Count.EqualTo(1), "루트 닫힘에 한 묶음 — 체인 전체가 한 트리");
        var entries = _batches[0];
        Assert.That(entries.Select(e => (e.Kind, e.Depth)), Is.EqualTo(new[]
        {
            (ActionLogEntryKind.ScopeStart, 0),   // BuyItem
            (ActionLogEntryKind.Data, 1),       // gold -500
            (ActionLogEntryKind.Data, 1),       // sword +1
            (ActionLogEntryKind.ScopeStart, 1),   // Achievement
            (ActionLogEntryKind.Note, 2),         // cleared
            (ActionLogEntryKind.Data, 2),       // gold +100 (보상)
        }));
        Assert.That(((InventoryChange)entries[1].Data!).Balance, Is.EqualTo((BigNum)500), "변경 후 잔량 — CS의 답");
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
        inventory.TryAdd(_gold, 100);   // 스코프 밖 변경
        _log.Log("standalone note");    // 스코프 밖 노트

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
            Assert.That(() => outer.Dispose(), Throws.InvalidOperationException, "역순 닫기 강제");
            inner.Dispose();
        }

        Assert.That(_batches, Has.Count.EqualTo(1));
        Assert.That(_batches[0].All(e => e.Kind == ActionLogEntryKind.ScopeStart), Is.True,
            "실패 커밋은 Change 미기록");
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
            "로그 미참조 인벤토리는 기록 안 됨");
    }
}
