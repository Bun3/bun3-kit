using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 게임 정의를 보관하는 카탈로그. 정의 스키마 <typeparamref name="TDefinition"/>은
    /// 게임 몫이며 프레임워크는 불투명하게 저장만 한다.
    /// <see cref="ItemCatalogBuilder{TDefinition}"/>으로 기동 시 1회 빌드한다.
    /// </summary>
    /// <typeparam name="TDefinition">게임이 정의하는 아이템 정의 타입.</typeparam>
    public sealed class ItemCatalog<TDefinition> : ItemCatalog
    {
        private readonly TDefinition[] _definitions;

        internal ItemCatalog(
            string[] ids,
            long[] maxStacks,
            Dictionary<string, int> lookup,
            TDefinition[] definitions)
            : base(ids, maxStacks, lookup)
        {
            _definitions = definitions;
        }

        /// <summary>정의를 반환한다. 무효 식별자면 던진다.</summary>
        public TDefinition GetDefinition(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "이 카탈로그의 식별자가 아닙니다.");
            }

            return _definitions[item.Index];
        }
    }
}
