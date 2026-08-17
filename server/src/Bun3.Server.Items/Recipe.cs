using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>레시피의 재료 또는 결과 1건 — 정의와 양수 수량.</summary>
    public readonly struct RecipeEntry
    {
        /// <summary>항목을 만든다. 무효 아이템·0 이하 수량은 던진다(기동 시 데이터 오류 조기 노출).</summary>
        public RecipeEntry(ItemId item, BigNum amount)
        {
            if (item.IsNone)
            {
                throw new ArgumentException("레시피 항목의 아이템이 None입니다.", nameof(item));
            }

            if (amount.Sign <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "레시피 항목 수량은 양수여야 합니다.");
            }

            Item = item;
            Amount = amount;
        }

        /// <summary>대상 아이템.</summary>
        public ItemId Item { get; }

        /// <summary>수량(양수).</summary>
        public BigNum Amount { get; }
    }

    /// <summary>
    /// 재료 목록 → 결과 목록의 제작 레시피(idlez materialItemGroups·growninja/Steam
    /// exchange의 공통 코어). 실행은 <see cref="ItemInventory{TState}.TryCraft"/> — 재료
    /// 소모와 결과 지급이 전부-아니면-전무다. 레시피 데이터는 게임 몫이며 보통
    /// TDefinition 안에 두고 카탈로그 검증 델리게이트에서 빌드 시 해석·검증한다.
    /// 게임 지식이 필요한 변형 — 대체 재료 분기(OR), 재료 유효성(레벨 요건),
    /// 특정 인스턴스 재료 지정 — 은 게임이 분기를 고른 뒤 이 레시피를 쓰거나
    /// <see cref="ItemInventory{TState}.BeginTransaction"/>으로 직접 조합한다.
    /// </summary>
    public sealed class Recipe
    {
        private readonly RecipeEntry[] _ingredients;
        private readonly RecipeEntry[] _results;

        /// <summary>레시피를 만든다(배열은 복사 — 기동 시 1회). 빈 배열 허용.</summary>
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

        /// <summary>소모할 재료 목록.</summary>
        public ReadOnlySpan<RecipeEntry> Ingredients => _ingredients;

        /// <summary>지급할 결과 목록.</summary>
        public ReadOnlySpan<RecipeEntry> Results => _results;
    }
}
