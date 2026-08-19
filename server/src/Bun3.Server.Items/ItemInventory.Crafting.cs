// ItemInventory partial — recipe crafting (TryCraft).
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    // Recipe execution — a thin composition over the commit core.
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>
        /// Executes a recipe — ingredient consumption and result grants are all-or-nothing.
        /// Ingredients are consumed before results are granted, so a definition appearing in both
        /// (equipment fusion, etc.) is resolved correctly under sequential semantics.
        /// <paramref name="failedIndex"/> counts ingredients first (from 0), then results.
        /// <paramref name="count"/> is the batch-craft multiplier (amounts × count).
        /// </summary>
        public InventoryError TryCraft(
            Recipe recipe,
            out int failedIndex,
            List<ItemInstance<TState>>? created = null,
            int count = 1)
        {
            failedIndex = -1;
            if (recipe == null)
            {
                throw new System.ArgumentNullException(nameof(recipe));
            }

            if (count <= 0)
            {
                return InventoryError.InvalidAmount;
            }

            var multiplier = (BigNum)count;
            _applyOps.Clear();
            var ingredients = recipe.Ingredients;
            for (var i = 0; i < ingredients.Length; i++)
            {
                _applyOps.Add(new TxOp(
                    TxOpKind.RemoveByItem, ingredients[i].Item, 0, ingredients[i].Amount * multiplier));
            }

            var results = recipe.Results;
            for (var i = 0; i < results.Length; i++)
            {
                _applyOps.Add(new TxOp(
                    TxOpKind.Add, results[i].Item, 0, results[i].Amount * multiplier));
            }

            return CommitOps(_applyOps, out failedIndex, created);
        }
    }
}
