using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 카탈로그 보조 색인 — 정의의 키(타입·태그·카테고리 등)로 <see cref="ItemId"/> 목록을
    /// 무할당 조회한다. <see cref="ItemCatalogBuilder{TDefinition}.CreateIndex{TKey}"/>로
    /// 선언하고 카탈로그 <c>Build()</c> 시 1회 구축된다. 하드코딩 문자열 조회 대신 이 색인과
    /// 빌드 시 해석·캐시한 정의 내 참조가 정상 접근 경로다.
    /// </summary>
    /// <typeparam name="TKey">색인 키 타입.</typeparam>
    public sealed class ItemCatalogIndex<TKey> where TKey : notnull
    {
        private Dictionary<TKey, ItemId[]>? _map;

        internal ItemCatalogIndex()
        {
        }

        /// <summary>키에 해당하는 아이템들을 반환한다. 미등록 키는 빈 span.
        /// 카탈로그 Build 전 호출은 던진다(기동 순서 오류).</summary>
        public ReadOnlySpan<ItemId> Get(TKey key)
        {
            if (_map == null)
            {
                throw new InvalidOperationException("카탈로그 Build 전에는 색인을 조회할 수 없습니다.");
            }

            return _map.TryGetValue(key, out var items) ? items : ReadOnlySpan<ItemId>.Empty;
        }

        /// <summary>키가 색인에 존재하는지 여부. Build 전 호출은 던진다.</summary>
        public bool Contains(TKey key)
        {
            if (_map == null)
            {
                throw new InvalidOperationException("카탈로그 Build 전에는 색인을 조회할 수 없습니다.");
            }

            return _map.ContainsKey(key);
        }

        internal void Build(Dictionary<TKey, List<ItemId>> entries)
        {
            var map = new Dictionary<TKey, ItemId[]>(entries.Count);
            foreach (var entry in entries)
            {
                map.Add(entry.Key, entry.Value.ToArray());
            }

            _map = map;
        }
    }
}
