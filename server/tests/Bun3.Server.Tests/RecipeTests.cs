using Bun3.Gameplay.Numerics;
using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class RecipeTests
{
    private sealed class ItemState;

    private ItemCatalog<string> _catalog = null!;
    private ItemId _gold;      // stackable
    private ItemId _ore;       // stackable
    private ItemId _potion;    // stackable, maxCount 10
    private ItemId _sword;     // unstackable
    private long _nextId;
    private ItemInventory<ItemState> _inventory = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ItemCatalogBuilder<string>()
            .Register("gold", "Gold")
            .Register("ore", "Ore")
            .Register("potion", "Potion", maxCount: 10)
            .Register("sword", "Sword", unstackable: true)
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
        _inventory.TryAdd(_gold, 100);   // no ore
        var recipe = new Recipe(
            new[] { new RecipeEntry(_gold, 30), new RecipeEntry(_ore, 3) },
            new[] { new RecipeEntry(_sword, 1) });

        Assert.That(_inventory.TryCraft(recipe, out var failedIndex), Is.EqualTo(InventoryError.Insufficient));
        Assert.That(failedIndex, Is.EqualTo(1), "ingredient index");
        Assert.That(_inventory.GetQuantity(_gold), Is.EqualTo((BigNum)100));
        Assert.That(_inventory.InstanceCount, Is.EqualTo(1));

        // Result cap overflow also leaves no changes — failedIndex is the result index after ingredients
        _inventory.TryAdd(_ore, 3);
        _inventory.TryAdd(_potion, 9);
        var potionRecipe = new Recipe(
            new[] { new RecipeEntry(_ore, 1) },
            new[] { new RecipeEntry(_potion, 5) });   // 9+5 > 10
        Assert.That(_inventory.TryCraft(potionRecipe, out failedIndex), Is.EqualTo(InventoryError.ExceedsMaxCount));
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
        // 3 swords → 1 sword fusion — ingredients settle before results are granted
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
