using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 아이템 정의 카탈로그의 비제네릭 코어 — 기동 시 1회 빌드되는 불변 인터닝 표.
    /// idlez류의 "ResourceItem 표"에 해당하는 포지션으로, 문자열 id ↔ <see cref="ItemId"/>
    /// 변환과 프레임워크 메타데이터(maxStack, 외부 숫자 id)만 안다.
    /// 정식 키는 문자열 id다 — 저장 데이터에는 항상 문자열 id를 쓰고, <see cref="ItemId"/>
    /// (등록 순서 의존 인덱스)는 절대 저장하지 않는다. DB·Steam(itemdefid) 등 숫자 키
    /// 체계와의 연동은 선택적 외부 id 역색인(<see cref="TryGetByExternalId"/>)으로 한다.
    /// 게임 정의 스키마는 <see cref="ItemCatalog{TDefinition}"/>이 보관한다.
    /// 컨테이너는 이 코어만 참조한다(수량 로직은 정의 무관).
    /// </summary>
    public class ItemCatalog
    {
        internal const long NoExternalId = long.MinValue;

        private readonly string[] _ids;
        private readonly long[] _maxStacks;
        private readonly long[] _externalIds;
        private readonly bool[] _unstackables;
        private readonly long[] _regenPeriods;
        private readonly ItemId[] _regenItems;
        private readonly Dictionary<string, int> _lookup;
        private readonly Dictionary<long, int> _externalLookup;

        internal ItemCatalog(
            string[] ids,
            long[] maxStacks,
            long[] externalIds,
            bool[] unstackables,
            long[] regenPeriods,
            ItemId[] regenItems,
            Dictionary<string, int> lookup,
            Dictionary<long, int> externalLookup)
        {
            _ids = ids;
            _maxStacks = maxStacks;
            _externalIds = externalIds;
            _unstackables = unstackables;
            _regenPeriods = regenPeriods;
            _regenItems = regenItems;
            _lookup = lookup;
            _externalLookup = externalLookup;
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

        /// <summary>정의당 최대 보유량. 무제한이면 <see cref="long.MaxValue"/>.
        /// 리젠 정의에서는 하드 상한이 아니라 리젠 목표선이다. 무효 식별자면 던진다.</summary>
        public long GetMaxStack(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "이 카탈로그의 식별자가 아닙니다.");
            }

            return _maxStacks[item.Index];
        }

        /// <summary>비스택형(인스턴스형) 정의인지 여부 — 스택/인스턴스 판정의 단일 원천.
        /// 무효 식별자면 던진다.</summary>
        public bool IsUnstackable(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "이 카탈로그의 식별자가 아닙니다.");
            }

            return _unstackables[item.Index];
        }

        /// <summary>리젠 주기(ticks). 0 = 리젠 없음. 무효 식별자면 던진다.</summary>
        public long GetRegenPeriodTicks(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "이 카탈로그의 식별자가 아닙니다.");
            }

            return _regenPeriods[item.Index];
        }

        /// <summary>리젠 메타가 등록된 정의 목록 — <see cref="ItemInventory{TState}.SettleRegen"/>의
        /// 순회 대상이자 게임의 리젠 기준 시각 저장 대상.</summary>
        public ReadOnlySpan<ItemId> RegenItems => _regenItems;

        /// <summary>외부 숫자 id(DB 컬럼·Steam itemdefid 등)를 반환한다.
        /// 미등록이면 false. 무효 식별자면 던진다.</summary>
        public bool TryGetExternalId(ItemId item, out long externalId)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "이 카탈로그의 식별자가 아닙니다.");
            }

            externalId = _externalIds[item.Index];
            return externalId != NoExternalId;
        }

        /// <summary>외부 숫자 id로 역조회한다. 미등록 id면 false, <paramref name="item"/>은 None.</summary>
        public bool TryGetByExternalId(long externalId, out ItemId item)
        {
            if (_externalLookup.TryGetValue(externalId, out var index))
            {
                item = new ItemId(index);
                return true;
            }

            item = ItemId.None;
            return false;
        }
    }
}
