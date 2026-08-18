using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

/// <summary>CS 감사 원장 — 로그 스코프 트리.</summary>
[TestFixture]
public class InventoryLogTests
{
    private sealed class ItemState;

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;
    private ItemId _sword;
    private long _nextId;
    private List<List<InventoryLogEntry>> _batches = null!;
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
        _batches = new List<List<InventoryLogEntry>>();
        _inventory = new ItemInventory<ItemState>(
            _catalog, () => ++_nextId, _ => new ItemState(),
            onLog: entries => _batches.Add(entries.ToArray().ToList()));
    }

    [Test]
    public void Scope_tree_captures_changes_notes_and_nesting()
    {
        _inventory.TryAdd(_gold, 1000);
        _batches.Clear();

        // 핸들러 지점 — 하위 로직은 문맥을 모른 채 자기 일만 한다
        using (_inventory.BeginLogScope("BuyItem product=1021 x1"))
        {
            _inventory.TryRemove(_gold, 500);              // 재료 소진 — 자동 첨부
            using (_inventory.BeginLogScope("PickReward"))
            {
                _inventory.Log("pity=3, roll=sword");      // 자유 노트
                _inventory.TryAdd(_sword, 1);              // 파생 지급 — 자동 첨부
            }
        }

        Assert.That(_batches, Has.Count.EqualTo(1), "루트 스코프 닫힘에 1묶음");
        var entries = _batches[0];
        Assert.That(entries.Select(e => (e.Kind, e.Depth)), Is.EqualTo(new[]
        {
            (InventoryLogEntryKind.ScopeStart, 0),   // BuyItem
            (InventoryLogEntryKind.Change, 1),       // gold -500
            (InventoryLogEntryKind.ScopeStart, 1),   // PickReward
            (InventoryLogEntryKind.Note, 2),         // pity 노트
            (InventoryLogEntryKind.Change, 2),       // sword +1
        }));
        Assert.That(entries[0].Text, Is.EqualTo("BuyItem product=1021 x1"));
        Assert.That(entries[1].Change.Delta, Is.EqualTo(-(BigNum)500));
        Assert.That(entries[1].Change.Balance, Is.EqualTo((BigNum)500), "변경 후 잔량 — CS의 답");
        Assert.That(entries[4].Change.Item, Is.EqualTo(_sword));
    }

    [Test]
    public void Unscoped_changes_flush_immediately_so_ledger_has_no_gaps()
    {
        _inventory.TryAdd(_gold, 100);   // 스코프 밖 — 즉시 단건 묶음

        Assert.That(_batches, Has.Count.EqualTo(1));
        Assert.That(_batches[0].Single().Kind, Is.EqualTo(InventoryLogEntryKind.Change));
        Assert.That(_batches[0].Single().Depth, Is.EqualTo(0));
    }

    [Test]
    public void Failed_commits_and_out_of_order_dispose_are_handled()
    {
        using (var scope = _inventory.BeginLogScope("action"))
        {
            Assert.That(_inventory.TryRemove(_gold, 5), Is.EqualTo(InventoryError.Insufficient));

            var inner = _inventory.BeginLogScope("inner");
            Assert.That(() => scope.Dispose(), Throws.InvalidOperationException, "역순 닫기 강제");
            inner.Dispose();
        }

        Assert.That(_batches, Has.Count.EqualTo(1), "실패 커밋은 Change 없이 스코프 항목만");
        Assert.That(_batches[0].All(e => e.Kind == InventoryLogEntryKind.ScopeStart), Is.True);
    }

    [Test]
    public void No_sink_means_no_op_scopes()
    {
        var silent = new ItemInventory<ItemState>(_catalog, () => ++_nextId, _ => new ItemState());
        Assert.That(silent.IsLogging, Is.False);

        using (silent.BeginLogScope("x"))
        {
            silent.Log("y");
            Assert.That(silent.TryAdd(_gold, 1), Is.EqualTo(InventoryError.None));
        }
        // 예외 없이 전부 no-op이면 통과
    }
}
