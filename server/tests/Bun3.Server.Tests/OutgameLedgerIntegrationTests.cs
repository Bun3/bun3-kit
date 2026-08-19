using System.Linq;
using Bun3.Gameplay.Numerics;
using Bun3.Server.Achievements;
using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class OutgameLedgerIntegrationTests
{
    private sealed class ItemState
    {
    }

    private readonly List<ActionLogEntry> _entries = new();
    private ActionLog _log = null!;
    private Transaction _transaction = null!;
    private ItemCatalog<string> _items = null!;
    private ItemId _gold;
    private ItemId _reward;
    private ItemId _capped;
    private ItemInventory<ItemState> _inventory = null!;
    private AchievementCatalog<AchievementDefinition> _achievementCatalog = null!;
    private AchievementId _firstPurchase;
    private AchievementManager<AchievementDefinition> _achievements = null!;

    [SetUp]
    public void SetUp()
    {
        _entries.Clear();
        _log = new ActionLog(batch =>
        {
            for (var i = 0; i < batch.Length; i++)
            {
                _entries.Add(batch[i]);
            }
        });
        _transaction = new Transaction();

        _items = new ItemCatalogBuilder<string>()
            .Register("gold", "Gold")
            .Register("reward", "Reward")
            .Register("capped", "Capped", maxCount: 1)
            .Build();
        _gold = _items.GetRequired("gold");
        _reward = _items.GetRequired("reward");
        _capped = _items.GetRequired("capped");

        long next = 0;
        _inventory = new ItemInventory<ItemState>(
            _items,
            instanceIdIssuer: () => ++next,
            stateFactory: _ => new ItemState(),
            log: _log,
            logLabel: "bag",
            transaction: _transaction);

        _achievementCatalog = new AchievementCatalog<AchievementDefinition>(new[]
        {
            new AchievementDefinition("first_purchase", 1),
        });
        _firstPurchase = _achievementCatalog.GetRequired("first_purchase");
        _achievements = new AchievementManager<AchievementDefinition>(
            _achievementCatalog,
            log: _log,
            logLabel: "ach",
            transaction: _transaction);
    }

    [Test]
    public void One_scope_holds_the_whole_chain_from_both_modules()
    {
        _inventory.TryAdd(_gold, 1000);
        _entries.Clear();

        using (_log.BeginScope("BuyProduct id=1021 x1"))
        using (var scope = _transaction.Begin())
        {
            Assert.That(_inventory.TryRemove(_gold, 300), Is.EqualTo(InventoryError.None));
            Assert.That(_inventory.TryAdd(_reward, 1), Is.EqualTo(InventoryError.None));
            Assert.That(_achievements.Increase(_firstPurchase, 1), Is.EqualTo(1));
            scope.Commit();
        }

        Assert.That(_entries[0].Kind, Is.EqualTo(ActionLogEntryKind.ScopeStart));
        Assert.That(_entries[0].Text, Is.EqualTo("BuyProduct id=1021 x1"));

        var data = _entries.Where(e => e.Kind == ActionLogEntryKind.Data).ToList();
        Assert.That(data.Count, Is.EqualTo(3));
        Assert.That(data[0].Source, Is.EqualTo("bag"));
        Assert.That(data[1].Source, Is.EqualTo("bag"));
        Assert.That(data[2].Source, Is.EqualTo("ach"));

        var spend = (InventoryChange)data[0].Data!;
        Assert.That(spend.Delta.ToString(), Is.EqualTo(((BigNum)(-300)).ToString()));
        Assert.That(spend.Balance.ToString(), Is.EqualTo(((BigNum)700).ToString()));

        var achievement = (AchievementChange)data[2].Data!;
        Assert.That(achievement.Kind, Is.EqualTo(AchievementChangeKind.Completed));
        Assert.That(data.All(e => e.Depth == 1), Is.True);
    }

    [Test]
    public void Claim_rolls_back_when_the_reward_grant_fails()
    {
        _inventory.TryAdd(_capped, 1);            // capped definition is now full
        _achievements.Increase(_firstPurchase, 1);
        Assert.That(_achievements.GetClaimableCount(_firstPurchase), Is.EqualTo(1));
        _entries.Clear();

        var granted = false;
        using (_log.BeginScope("ClaimReward"))
        using (var scope = _transaction.Begin())
        {
            Assert.That(_achievements.TryClaim(_firstPurchase), Is.True);
            if (_inventory.TryAdd(_capped, 1) == InventoryError.None)
            {
                granted = true;
                scope.Commit();
            }
        }

        Assert.That(granted, Is.False);
        Assert.That(_achievements.GetClaimableCount(_firstPurchase), Is.EqualTo(1), "the claim must be restored");
        Assert.That(_achievements.GetState(_firstPurchase).ClaimedCount, Is.Zero);
        Assert.That(_entries.Where(e => e.Kind == ActionLogEntryKind.Data), Is.Empty, "a rolled-back change must not reach the ledger");
    }

    [Test]
    public void Claim_and_grant_commit_together_on_success()
    {
        _achievements.Increase(_firstPurchase, 1);

        using (var scope = _transaction.Begin())
        {
            Assert.That(_achievements.TryClaim(_firstPurchase), Is.True);
            Assert.That(_inventory.TryAdd(_reward, 10), Is.EqualTo(InventoryError.None));
            scope.Commit();
        }

        Assert.That(_achievements.GetState(_firstPurchase).ClaimedCount, Is.EqualTo(1));
        Assert.That(_inventory.GetQuantity(_reward).ToString(), Is.EqualTo(((BigNum)10).ToString()));
    }

    [Test]
    public void Hot_path_outside_a_transaction_stays_allocation_free()
    {
        var bare = new ItemInventory<ItemState>(
            _items,
            instanceIdIssuer: () => 1,
            stateFactory: _ => new ItemState(),
            transaction: _transaction);   // Injected but never opened — the configuration a game ships.
        bare.TryAdd(_gold, 1_000_000);

        for (var i = 0; i < 100; i++)
        {
            bare.TryAdd(_gold, 1);
            bare.TryRemove(_gold, 1);
            _ = bare.GetQuantity(_gold);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            bare.TryAdd(_gold, 1);
            bare.TryRemove(_gold, 1);
            _ = bare.GetQuantity(_gold);
        }

        var delta = GC.GetAllocatedBytesForCurrentThread() - before;
        if (delta != 0)
        {
            Assert.Fail($"Expected no allocation on the transaction-free hot path, got {delta} bytes.");
        }
    }
}
