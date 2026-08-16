#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// 하나의 카탈로그에 묶여 계층 태그 질의를 받는 컨테이너의 공통 기반입니다.
    /// </summary>
    public abstract class TagQueryContainer
    {
        private readonly TagCatalog _catalog;

        // internal 생성자 — 외부 파생을 막아 파생 타입의 sealed 의미를 유지한다.
        internal TagQueryContainer(TagCatalog catalog) => _catalog = catalog;

        internal TagCatalog Catalog => _catalog;

        /// <summary>조회 태그 자신 또는 그 자손이 이 컨테이너에 담겨 있는지 확인합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>일치하는 태그가 있으면 <see langword="true"/>입니다.</returns>
        public abstract bool Has(GameplayTag tag);

        /// <summary>조회 태그가 정확히 담겨 있는지 확인합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>정확히 일치하는 태그가 있으면 <see langword="true"/>입니다.</returns>
        public abstract bool HasExact(GameplayTag tag);

        /// <summary>계층형 조회 태그 중 하나라도 이 컨테이너와 일치하는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그 인스턴스에서 만든 조회 태그입니다.</param>
        /// <returns>조회 태그 중 하나라도 일치하면 <see langword="true"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그 인스턴스에 속한 경우입니다.</exception>
        public bool HasAny(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query.ExactKindCount; i++)
            {
                if (Has(new GameplayTag(query.GetExactIndexAt(i))))
                    return true;
            }

            return false;
        }

        /// <summary>계층형 조회 태그가 모두 이 컨테이너와 일치하는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그 인스턴스에서 만든 조회 태그입니다.</param>
        /// <returns>빈 조회를 포함해 모든 조회 태그가 일치하면 <see langword="true"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그 인스턴스에 속한 경우입니다.</exception>
        public bool HasAll(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query.ExactKindCount; i++)
            {
                if (!Has(new GameplayTag(query.GetExactIndexAt(i))))
                    return false;
            }

            return true;
        }

        /// <summary>정확한 조회 태그 중 하나라도 이 컨테이너에 있는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그 인스턴스에서 만든 조회 태그입니다.</param>
        /// <returns>정확한 조회 태그 중 하나라도 일치하면 <see langword="true"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그 인스턴스에 속한 경우입니다.</exception>
        public bool HasAnyExact(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query.ExactKindCount; i++)
            {
                if (HasExact(new GameplayTag(query.GetExactIndexAt(i))))
                    return true;
            }

            return false;
        }

        /// <summary>정확한 조회 태그가 모두 이 컨테이너에 있는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그 인스턴스에서 만든 조회 태그입니다.</param>
        /// <returns>빈 조회를 포함해 모든 정확한 조회 태그가 일치하면 <see langword="true"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그 인스턴스에 속한 경우입니다.</exception>
        public bool HasAllExact(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query.ExactKindCount; i++)
            {
                if (!HasExact(new GameplayTag(query.GetExactIndexAt(i))))
                    return false;
            }

            return true;
        }

        // 변경 경로 공통 검증.
        private protected void ValidateMutationTag(GameplayTag tag)
        {
            if (!tag.IsValid)
                throw new ArgumentException("GameplayTag.None은 컨테이너에 담을 수 없습니다.", nameof(tag));
        }

        private void ValidateQueryCatalog(TagContainer query)
        {
            if (query is null)
                throw new ArgumentNullException(nameof(query));
            if (!ReferenceEquals(_catalog, query.Catalog))
                throw new ArgumentException("태그 질의는 같은 TagCatalog 인스턴스를 요구합니다.", nameof(query));
        }
    }
}
