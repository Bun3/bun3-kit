#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Stores up to 64 distinct explicit gameplay tags from one catalog, in sorted wire-index order.</summary>
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

        /// <summary>Gets the number of explicitly stored tag kinds.</summary>
        public int ExactKindCount => _count;

        /// <summary>Copies the explicitly stored tags in ascending catalog-index order.</summary>
        /// <param name="destination">Buffer receiving the explicit tags.</param>
        /// <returns>Number of tags copied.</returns>
        /// <exception cref="ArgumentException">The buffer is shorter than the explicit tag kind count.</exception>
        public int CopyExactTags(Span<GameplayTag> destination)
        {
            if (destination.Length < _count)
            {
                throw new ArgumentException(
                    "Buffer is smaller than the explicit tag count.", nameof(destination));
            }

            for (var i = 0; i < _count; i++)
                destination[i] = new GameplayTag(_indices[i]);
            return _count;
        }

        internal ushort GetExactIndexAt(int position) => _indices[position];

        /// <summary>Adds a tag that is not yet explicitly stored.</summary>
        /// <param name="tag">Catalog tag to add.</param>
        /// <returns><see langword="true"/> if the tag was added; <see langword="false"/> if it was already stored.</returns>
        /// <exception cref="ArgumentException"><paramref name="tag"/> is <see cref="GameplayTag.None"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is outside the catalog range of this container.</exception>
        /// <exception cref="InvalidOperationException">The container already stores 64 explicit kinds.</exception>
        public bool Add(GameplayTag tag)
        {
            ValidateMutationTag(tag);
            var insertionIndex = TagSearch.LowerBound(_indices, _count, tag.Index, out _);
            if (insertionIndex < _count && _indices[insertionIndex] == tag.Index)
                return false;
            if (_count == 64)
                throw new InvalidOperationException("TagContainer can hold at most 64 explicit kinds.");

            EnsureCapacityForOneMore();
            if (insertionIndex < _count)
                Array.Copy(_indices, insertionIndex, _indices, insertionIndex + 1, _count - insertionIndex);
            _indices[insertionIndex] = tag.Index;
            _count++;
            return true;
        }

        /// <summary>Removes an explicitly stored tag.</summary>
        /// <param name="tag">Catalog tag to remove.</param>
        /// <returns><see langword="true"/> if the tag was removed; <see langword="false"/> if it was not stored.</returns>
        /// <exception cref="ArgumentException"><paramref name="tag"/> is <see cref="GameplayTag.None"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is outside the catalog range of this container.</exception>
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

        /// <summary>Checks whether an explicitly stored tag is the query tag itself or one of its descendants.</summary>
        /// <param name="tag">Tag to query.</param>
        /// <returns><see langword="true"/> if a matching explicit tag is stored.</returns>
        public override bool Has(GameplayTag tag) => HasCore(tag, exact: false, out _);

        /// <summary>Checks whether the query tag is explicitly stored.</summary>
        /// <param name="tag">Tag to query.</param>
        /// <returns><see langword="true"/> if an exactly matching tag is stored.</returns>
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
