#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>하나의 카탈로그에서 최대 64개의 고유한 명시적 게임플레이 태그를 정렬된 와이어 인덱스 순서로 저장합니다.</summary>
    public sealed class TagContainer : TagQueryContainer
    {
        private ushort[] _indices;
        private int _count;

        internal TagContainer(TagCatalog catalog, int expectedExactKinds)
            : base(catalog)
        {
            if ((uint)expectedExactKinds > 64u)
                throw new ArgumentOutOfRangeException(nameof(expectedExactKinds));

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
                    "버퍼가 명시 태그 수보다 작습니다.", nameof(destination));
            }

            for (var i = 0; i < _count; i++)
                destination[i] = new GameplayTag(_indices[i]);
            return _count;
        }

        internal ushort GetExactIndexAt(int position) => _indices[position];

        /// <summary>아직 명시적으로 저장되지 않은 태그를 추가합니다.</summary>
        /// <param name="tag">추가할 카탈로그 태그입니다.</param>
        /// <returns>태그가 추가되면 <see langword="true"/>이고, 이미 저장되어 있으면 <see langword="false"/>입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="tag"/>가 <see cref="GameplayTag.None"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/>가 이 컨테이너의 카탈로그 범위 밖인 경우입니다.</exception>
        /// <exception cref="InvalidOperationException">컨테이너에 이미 64개의 명시적 종류가 저장된 경우입니다.</exception>
        public bool Add(GameplayTag tag)
        {
            ValidateMutationTag(tag);
            var insertionIndex = TagSearch.LowerBound(_indices, _count, tag.Index, out _);
            if (insertionIndex < _count && _indices[insertionIndex] == tag.Index)
                return false;
            if (_count == 64)
                throw new InvalidOperationException("TagContainer는 명시적 종류를 64개까지만 담을 수 있습니다.");

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
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/>가 이 컨테이너의 카탈로그 범위 밖인 경우입니다.</exception>
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
        public override bool Has(GameplayTag tag) => HasCore(tag, exact: false, out _);

        /// <summary>조회 태그가 명시적으로 저장되어 있는지 확인합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>정확히 일치하는 태그가 저장되어 있으면 <see langword="true"/>입니다.</returns>
        public override bool HasExact(GameplayTag tag) => HasCore(tag, exact: true, out _);

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
            return exact ? _indices[index] == tag.Index : _indices[index] <= Catalog.GetSubtreeEnd(tag);
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
    }
}
