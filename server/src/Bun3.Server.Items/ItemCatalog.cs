using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 아이템 정의 카탈로그의 비제네릭 코어 — 기동 시 1회 빌드되는 불변 인터닝 표.
    /// 문자열 id ↔ <see cref="ItemId"/> 변환과 프레임워크 메타데이터(maxStack)만 안다.
    /// 게임 정의 스키마는 <see cref="ItemCatalog{TDefinition}"/>이 보관한다.
    /// 컨테이너는 이 코어만 참조한다(수량 로직은 정의 무관).
    /// </summary>
    public class ItemCatalog
    {
        private readonly string[] _ids;
        private readonly long[] _maxStacks;
        private readonly Dictionary<string, int> _lookup;

        internal ItemCatalog(string[] ids, long[] maxStacks, Dictionary<string, int> lookup)
        {
            _ids = ids;
            _maxStacks = maxStacks;
            _lookup = lookup;
        }

        /// <summary>등록된 정의 수.</summary>
        public int Count => _ids.Length;

        /// <summary>문자열 id로 조회한다. 없으면 false, <paramref name="item"/>은 None.</summary>
        public bool TryGet(string id, out ItemId item)
        {
            if (id != null && _lookup.TryGetValue(id, out var index))
            {
                item = new ItemId(index);
                return true;
            }

            item = ItemId.None;
            return false;
        }

        /// <summary>문자열 id로 조회한다. 없으면 <see cref="ItemCatalogException"/>.</summary>
        public ItemId GetRequired(string id)
        {
            if (!TryGet(id, out var item))
            {
                throw new ItemCatalogException($"카탈로그에 없는 아이템 id: '{id}'");
            }

            return item;
        }

        /// <summary>이 카탈로그의 유효한 식별자인지 여부(None·범위 밖은 false).</summary>
        public bool Contains(ItemId item) => (uint)item.Index < (uint)_ids.Length;

        /// <summary>인터닝된 문자열 id를 반환한다 — 무할당. 무효 식별자면 던진다.</summary>
        public string GetIdString(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "이 카탈로그의 식별자가 아닙니다.");
            }

            return _ids[item.Index];
        }

        /// <summary>스택 상한. 무제한이면 <see cref="long.MaxValue"/>. 무효 식별자면 던진다.</summary>
        public long GetMaxStack(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "이 카탈로그의 식별자가 아닙니다.");
            }

            return _maxStacks[item.Index];
        }
    }
}
