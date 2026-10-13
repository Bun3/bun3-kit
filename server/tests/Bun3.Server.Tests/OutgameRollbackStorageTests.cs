using Bun3.Server.Achievements;
using Bun3.Server.Core;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class OutgameRollbackStorageTests
{
    private sealed class ItemState { public int Level; }

    [TestCase(false)]
    [TestCase(true)]
    public void Achievement_constructor_allocates_only_the_required_state_array_as_catalog_grows(bool inactiveTransaction)
    {
        const int small = 64;
        const int large = 4096;
        const int repetitions = 8;
        var smallCatalog = Catalog(small);
        var largeCatalog = Catalog(large);
        var transaction = inactiveTransaction ? new Transaction() : null;

        _ = new AchievementManager<AchievementDefinition>(smallCatalog, transaction: transaction);
        _ = new AchievementManager<AchievementDefinition>(largeCatalog, transaction: transaction);
        var smallBytes = MeasureConstructors(smallCatalog, transaction, repetitions);
        var largeBytes = MeasureConstructors(largeCatalog, transaction, repetitions);

        var beforeArrays = GC.GetAllocatedBytesForCurrentThread();
        var smallStates = new AchievementState[small];
        var afterSmall = GC.GetAllocatedBytesForCurrentThread();
        var largeStates = new AchievementState[large];
        var afterLarge = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(smallStates);
        GC.KeepAlive(largeStates);
        var stateGrowth = (afterLarge - afterSmall) - (afterSmall - beforeArrays);

        var beforeFlags = GC.GetAllocatedBytesForCurrentThread();
        var smallFlags = new bool[small];
        var afterSmallFlags = GC.GetAllocatedBytesForCurrentThread();
        var largeFlags = new bool[large];
        var afterLargeFlags = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(smallFlags);
        GC.KeepAlive(largeFlags);
        var flagGrowth = (afterLargeFlags - afterSmallFlags) - (afterSmallFlags - beforeFlags);

        var observedGrowth = (largeBytes - smallBytes) / repetitions;
        Assert.That(observedGrowth, Is.LessThanOrEqualTo(stateGrowth + flagGrowth / 2),
            "A dormant manager should not allocate a second catalog-sized rollback array.");
    }

    [Test]
    public void Empty_transaction_and_standalone_item_scope_keep_immediate_behavior_without_allocating()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var item = catalog.GetRequired("sword");
        var transaction = new Transaction();
        var dirty = 0;
        long next = 0;
        var inventory = new ItemInventory<ItemState>(catalog, () => ++next, _ => new ItemState(),
            onDirty: () => dirty++, transaction: transaction);
        inventory.TryAdd(item, 1);
        var instances = new List<ItemInstance<ItemState>>();
        inventory.CollectInstances(item, instances);
        var instance = instances[0];

        for (var i = 0; i < 100; i++)
        {
            using (transaction.Begin()) { }
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            using (transaction.Begin()) { }
        }
        var emptyBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        var beforeDirty = dirty;
        using (var scope = instance.BeginStateChange())
        {
            scope.State.Level++;
        }
        Assert.That(emptyBytes, Is.Zero);
        Assert.That(dirty, Is.EqualTo(beforeDirty + 1));
        Assert.That(instance.State.Level, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Ordinary_item_and_achievement_mutations_stay_allocation_free_and_notify_immediately(bool inactiveTransaction)
    {
        var transaction = inactiveTransaction ? new Transaction() : null;
        var itemCatalog = new ItemCatalogBuilder<string>().Register("gold", "Gold").Build();
        var gold = itemCatalog.GetRequired("gold");
        long next = 0;
        var itemDirty = 0;
        Func<long> issueId = () => ++next;
        Func<ItemId, int> createState = _ => 0;
        Action itemNotification = () => itemDirty++;
        var inventory = new ItemInventory<int>(itemCatalog, issueId, createState,
            onDirty: itemNotification, transaction: transaction);
        var achievementCatalog = new AchievementCatalog<AchievementDefinition>(
            new[] { new AchievementDefinition("progress", long.MaxValue) });
        var achievementId = achievementCatalog.GetRequired("progress");
        var achievementDirty = 0;
        Action achievementNotification = () => achievementDirty++;
        var achievements = new AchievementManager<AchievementDefinition>(achievementCatalog,
            onDirty: achievementNotification, transaction: transaction);

        for (var i = 0; i < 100; i++)
        {
            inventory.TryAdd(gold, 1);
            achievements.Increase(achievementId, 1);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            inventory.TryAdd(gold, 1);
            achievements.Increase(achievementId, 1);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero);
        Assert.That(itemDirty, Is.EqualTo(1100));
        Assert.That(achievementDirty, Is.EqualTo(1100));
        Assert.That(inventory.GetQuantity(gold), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)1100));
        Assert.That(achievements.GetState(achievementId).Progress, Is.EqualTo(1100));
    }

    [Test]
    public void First_active_item_mutation_allocates_rollback_capacity_then_reuses_it_after_rollback_and_commit()
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var sword = catalog.GetRequired("sword");
        var transaction = new Transaction();
        long next = 0;
        var inventory = new ItemInventory<int>(catalog, () => ++next, _ => 0, transaction: transaction);
        inventory.TryAdd(sword, 1);
        var instances = new List<ItemInstance<int>>();
        inventory.CollectInstances(sword, instances);
        var instance = instances[0];

        using (transaction.Begin()) { }
        var before = GC.GetAllocatedBytesForCurrentThread();
        using (transaction.Begin()) instance.Flags = 1;
        var firstMutationBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(instance.Flags, Is.Zero);

        before = GC.GetAllocatedBytesForCurrentThread();
        using (var scope = transaction.Begin())
        {
            instance.Flags = 1;
            scope.Commit();
        }
        var afterCommitBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(instance.Flags, Is.EqualTo(1));

        before = GC.GetAllocatedBytesForCurrentThread();
        using (transaction.Begin()) instance.Flags = 2;
        var afterRollbackBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(firstMutationBytes, Is.GreaterThan(0));
        Assert.That(afterCommitBytes, Is.LessThan(firstMutationBytes / 4));
        Assert.That(afterRollbackBytes, Is.LessThan(firstMutationBytes / 4));
        Assert.That(instance.Flags, Is.EqualTo(1));
    }

    private static AchievementCatalog<AchievementDefinition> Catalog(int count)
    {
        var definitions = new AchievementDefinition[count];
        for (var i = 0; i < count; i++) definitions[i] = new AchievementDefinition(i.ToString(), 1);
        return new AchievementCatalog<AchievementDefinition>(definitions);
    }

    private static long MeasureConstructors(AchievementCatalog<AchievementDefinition> catalog,
        Transaction? transaction, int repetitions)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        AchievementManager<AchievementDefinition>? last = null;
        for (var i = 0; i < repetitions; i++)
            last = new AchievementManager<AchievementDefinition>(catalog, transaction: transaction);
        GC.KeepAlive(last);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
