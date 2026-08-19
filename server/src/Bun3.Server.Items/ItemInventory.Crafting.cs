// ItemInventory partial — 레시피 제작(TryCraft) 담당.
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    // 레시피 실행 — 커밋 코어 위의 얇은 조합.
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>
        /// 레시피를 실행한다 — 재료 소모와 결과 지급이 전부-아니면-전무. 재료가 먼저
        /// 소모된 뒤 결과가 지급되므로 같은 정의가 재료·결과에 다 있어도(장비 합성류)
        /// 순차 의미론으로 정확히 판정된다. <paramref name="failedIndex"/>는 재료(0부터),
        /// 이어서 결과 순번이다. <paramref name="count"/>는 반복 제작 배수(수량 × count).
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
