using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>One ingredient or result of a recipe — a definition and a positive amount.</summary>
    public readonly struct RecipeEntry
    {
        /// <summary>Creates the entry. Invalid items and non-positive amounts throw (surfacing data
        /// errors early at startup).</summary>
        public RecipeEntry(ItemId item, BigNum amount)
        {
            if (item.IsNone)
            {
                throw new ArgumentException("Recipe entry item is None.", nameof(item));
            }

            if (amount.Sign <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "Recipe entry amount must be positive.");
            }

            Item = item;
            Amount = amount;
        }

        /// <summary>Target item.</summary>
        public ItemId Item { get; }

        /// <summary>Amount (positive).</summary>
        public BigNum Amount { get; }
    }

    /// <summary>
    /// Crafting recipe: ingredient list → result list. Executed via
    /// <see cref="ItemInventory{TState}.TryCraft"/> — ingredient consumption and result grants are
    /// all-or-nothing. Recipe data belongs to the game, typically kept inside TDefinition and
    /// resolved/validated at build time by catalog validator delegates.
    /// Variants requiring game knowledge — alternative-ingredient branches (OR), ingredient
    /// eligibility (level requirements), specific-instance ingredients — are handled by the game
    /// choosing the branch and then using this recipe, or composing directly via
    /// <see cref="ItemInventory{TState}.BeginTransaction"/>.
    /// </summary>
    public sealed class Recipe
    {
        private readonly RecipeEntry[] _ingredients;
        private readonly RecipeEntry[] _results;

        /// <summary>Creates the recipe (arrays are copied — once at startup). Empty arrays allowed.</summary>
        public Recipe(RecipeEntry[] ingredients, RecipeEntry[] results)
        {
            if (ingredients == null)
            {
                throw new ArgumentNullException(nameof(ingredients));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            _ingredients = (RecipeEntry[])ingredients.Clone();
            _results = (RecipeEntry[])results.Clone();
        }

        /// <summary>Ingredients to consume.</summary>
        public ReadOnlySpan<RecipeEntry> Ingredients => _ingredients;

        /// <summary>Results to grant.</summary>
        public ReadOnlySpan<RecipeEntry> Results => _results;
    }
}
