using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class RecipeTests
{
    private sealed class ItemState;

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;      // 스택형
    private ItemId _ore;       // 스택형
    private ItemId _potion;    // 스택형, maxStack 10
    private ItemId _sword;     // 비스택형
    private long _nextId;
    private ItemInventory<ItemState> _inventory = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "골드")
            .Register("ore", "광석")
            .Register("potion", "물약", maxStack: 10)
            .Register("sword", "검", unstackable: true)
            .Build();
        _gold = _catalog.GetRequired("gold");
        _ore = _catalog.GetRequired("ore");
        _potion = _catalog.GetRequired("potion");
        _sword = _catalog.GetRequired("sword");
        _nextId = 0;
        _inventory = new ItemInventory<ItemState>(
            _catalog, () => ++_nextId, _ => new ItemState());
    }

    [Test]
    public void Craft_consumes_ingredients_and_grants_results_atomically()
    {
        _inventory.TryAdd(_gold, 100);
        _inventory.TryAdd(_ore, 5);
        var recipe = new Recipe(
            new[] { new RecipeEntry(_gold, 30), new RecipeEntry(_ore, 3) },
            new[] { new RecipeEntry(_sword, 1) });
        var created = new List<ItemInstance<ItemState>>();

        Assert.That(_inventory.TryCraft(recipe, out _, created), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)70));
        Assert.That(_inventory.GetQuantity(_ore), Is.EqualTo((BigNum)2));
        Assert.That(created, Has.Count.EqualTo(1));
        Assert.That(created[0].Item, Is.EqualTo(_sword));
    }

    [Test]
    public void Craft_failure_points_at_entry_and_leaves_inventory_untouched()
    {
        _inventory.TryAdd(_gold, 100);   // 광석 없음
        var recipe = new Recipe(
            new[] { new RecipeEntry(_gold, 30), new RecipeEntry(_ore, 3) },
            new[] { new RecipeEntry(_sword, 1) });

        Assert.That(_inventory.TryCraft(recipe, out var failedIndex), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(1), "재료 순번");
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)100));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(1));

        // 결과 상한 초과도 무변경 — failedIndex는 재료 이후 결과 순번
        _inventory.TryAdd(_ore, 3);
        _inventory.TryAdd(_potion, 9);
        var potionRecipe = new Recipe(
            new[] { new RecipeEntry(_ore, 1) },
            new[] { new RecipeEntry(_potion, 5) });   // 9+5 > 10
        Assert.That(_inventory.TryCraft(potionRecipe, out failedIndex), Is.EqualTo(InventoryError.ExceedsMaxStack));
        Assert.That(failedIndex, Is.EqualTo(1));
        Assert.That(_inventory.GetQuantity(_ore), Is.EqualTo((BigNum)3));
    }

    [Test]
    public void Craft_count_multiplies_ingredients_and_results()
    {
        _inventory.TryAdd(_ore, 10);
        var recipe = new Recipe(
            new[] { new RecipeEntry(_ore, 2) },
            new[] { new RecipeEntry(_potion, 1) });

        Assert.That(_inventory.TryCraft(recipe, out _, count: 4), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_ore), Is.EqualTo((BigNum)2));
        Assert.That(_inventory.GetQuantity(_potion), Is.EqualTo((BigNum)4));
        Assert.That(_inventory.TryCraft(recipe, out _, count: 0), Is.EqualTo(InventoryError.InvalidAmount));
    }

    [Test]
    public void Craft_supports_same_item_as_ingredient_and_result()
    {
        // 검 3자루 → 검 1자루 합성 — 재료 소모가 결과 지급보다 먼저 정산된다
        _inventory.TryAdd(_sword, 3);
        var recipe = new Recipe(
            new[] { new RecipeEntry(_sword, 3) },
            new[] { new RecipeEntry(_sword, 1) });

        Assert.That(_inventory.TryCraft(recipe, out _), Is.EqualTo(InventoryError.None));
        Assert.That(_inventory.GetQuantity(_sword), Is.EqualTo(BigNum.One));
    }

    [Test]
    public void Recipe_entries_reject_invalid_data_at_construction()
    {
        Assert.That(() => new RecipeEntry(ItemId.None, 1), Throws.ArgumentException);
        Assert.That(() => new RecipeEntry(_gold, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
