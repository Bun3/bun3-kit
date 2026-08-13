#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>하나의 카탈로그에서 최대 64개의 고유한 명시적 게임플레이 태그를 정렬된 와이어 인덱스 순서로 저장합니다.</summary>
    public sealed class TagContainer
    {
        private readonly TagCatalog _catalog;
        private ushort[] _indices;
        private int _count;

        internal TagContainer(TagCatalog catalog, int expectedExactKinds)
        {
            if ((uint)expectedExactKinds > 64u)
                throw new ArgumentOutOfRangeException(nameof(expectedExactKinds));

            _catalog = catalog;
            _indices = expectedExactKinds == 0 ? Array.Empty<ushort>() : new ushort[expectedExactKinds];
        }

        /// <summary>명시적으로 저장된 태그 종류 수를 가져옵니다.</summary>
        public int ExactKindCount => _count;

        /// <summary>명시적으로 저장된 태그를 카탈로그 인덱스 오름차순으로 복사합니다.</summary>
        /// <param name="destination">명시 태그를 받을 버퍼입니다.</param>
        /// <returns>복사한 태그 수입니다.</returns>
        /// <exception cref="ArgumentException">버퍼 길이가 명시 태그 종류 수보다 작은 경우입니다.</exception>
        public int CopyExactTags(Span<GameplayTag> destination)
        {
            if (destination.Length < _count)
            {
                throw new ArgumentException(
                    "The destination is too small for the exact tags.", nameof(destination));
            }

            for (var i = 0; i < _count; i++)
                destination[i] = new GameplayTag(_indices[i]);
            return _count;
        }

        internal TagCatalog Catalog => _catalog;

        internal ushort GetExactIndexAt(int position) => _indices[position];

        /// <summary>아직 명시적으로 저장되지 않은 태그를 추가합니다.</summary>
        /// <param name="tag">추가할 카탈로그 태그입니다.</param>
        /// <returns>태그가 추가되면 <see langword="true"/>이고, 이미 저장되어 있으면 <see langword="false"/>입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="tag"/>가 <see cref="GameplayTag.None"/>인 경우입니다.</exception>
        /// <exception cref="InvalidOperationException">컨테이너에 이미 64개의 명시적 종류가 저장된 경우입니다.</exception>
        public bool Add(GameplayTag tag)
        {
            ValidateMutationTag(tag);
            var insertionIndex = TagSearch.LowerBound(_indices, _count, tag.Index, out _);
            if (insertionIndex < _count && _indices[insertionIndex] == tag.Index)
                return false;
            if (_count == 64)
                throw new InvalidOperationException("A TagContainer cannot hold more than 64 explicit kinds.");

            EnsureCapacityForOneMore();
            if (insertionIndex < _count)
                Array.Copy(_indices, insertionIndex, _indices, insertionIndex + 1, _count - insertionIndex);
            _indices[insertionIndex] = tag.Index;
            _count++;
            return true;
        }

        /// <summary>명시적으로 저장된 태그를 제거합니다.</summary>
        /// <param name="tag">제거할 카탈로그 태그입니다.</param>
        /// <returns>태그가 제거되면 <see langword="true"/>이고, 저장되어 있지 않으면 <see langword="false"/>입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="tag"/>가 <see cref="GameplayTag.None"/>인 경우입니다.</exception>
        public bool Remove(GameplayTag tag)
        {
            ValidateMutationTag(tag);
            var removalIndex = TagSearch.LowerBound(_indices, _count, tag.Index, out _);
            if (removalIndex == _count || _indices[removalIndex] != tag.Index)
                return false;

            var elementsAfter = _count - removalIndex - 1;
            if (elementsAfter != 0)
                Array.Copy(_indices, removalIndex + 1, _indices, removalIndex, elementsAfter);
            _indices[--_count] = 0;
            return true;
        }

        /// <summary>명시적으로 저장된 태그가 조회 태그 자신이거나 그 자손인지 확인합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>일치하는 명시적 태그가 저장되어 있으면 <see langword="true"/>입니다.</returns>
        public bool Has(GameplayTag tag) => HasCore(tag, exact: false, out _);

        /// <summary>조회 태그가 명시적으로 저장되어 있는지 확인합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>정확히 일치하는 태그가 저장되어 있으면 <see langword="true"/>입니다.</returns>
        public bool HasExact(GameplayTag tag) => HasCore(tag, exact: true, out _);

        /// <summary>계층형 조회 태그 중 하나라도 이 컨테이너와 일치하는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그 인스턴스에서 만든 조회 태그입니다.</param>
        /// <returns>조회 태그 중 하나라도 일치하면 <see langword="true"/>이고, 그렇지 않으면 <see langword="false"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그 인스턴스에 속한 경우입니다.</exception>
        public bool HasAny(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query._count; i++)
            {
                if (Has(new GameplayTag(query._indices[i])))
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
            for (var i = 0; i < query._count; i++)
            {
                if (!Has(new GameplayTag(query._indices[i])))
                    return false;
            }

            return true;
        }

        /// <summary>정확한 조회 태그 중 하나라도 명시적으로 저장되어 있는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그 인스턴스에서 만든 조회 태그입니다.</param>
        /// <returns>정확한 조회 태그 중 하나라도 일치하면 <see langword="true"/>이고, 그렇지 않으면 <see langword="false"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그 인스턴스에 속한 경우입니다.</exception>
        public bool HasAnyExact(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query._count; i++)
            {
                if (HasExact(new GameplayTag(query._indices[i])))
                    return true;
            }

            return false;
        }

        /// <summary>정확한 조회 태그가 모두 명시적으로 저장되어 있는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그 인스턴스에서 만든 조회 태그입니다.</param>
        /// <returns>빈 조회를 포함해 모든 정확한 조회 태그가 일치하면 <see langword="true"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그 인스턴스에 속한 경우입니다.</exception>
        public bool HasAllExact(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query._count; i++)
            {
                if (!HasExact(new GameplayTag(query._indices[i])))
                    return false;
            }

            return true;
        }

        internal bool HasCore(GameplayTag tag, bool exact, out int comparisons)
        {
            if (!tag.IsValid)
            {
                comparisons = 0;
                return false;
            }

            var index = TagSearch.LowerBound(_indices, _count, tag.Index, out comparisons);
            if (index == _count)
                return false;
            return exact ? _indices[index] == tag.Index : _indices[index] <= _catalog.GetSubtreeEnd(tag);
        }

        private void EnsureCapacityForOneMore()
        {
            if (_count < _indices.Length)
                return;

            var newCapacity = _indices.Length == 0 ? 4 : _indices.Length * 2;
            if (newCapacity > 64)
                newCapacity = 64;
            var expanded = new ushort[newCapacity];
            Array.Copy(_indices, expanded, _count);
            _indices = expanded;
        }

        private static void ValidateMutationTag(GameplayTag tag)
        {
            if (!tag.IsValid)
                throw new ArgumentException("GameplayTag.None cannot be stored in a TagContainer.", nameof(tag));
        }

        private void ValidateQueryCatalog(TagContainer query)
        {
            if (query is null)
                throw new ArgumentNullException(nameof(query));
            if (!ReferenceEquals(_catalog, query._catalog))
                throw new ArgumentException("TagContainer queries require the same TagCatalog instance.", nameof(query));
        }
    }
}
