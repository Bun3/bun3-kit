#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// Stores up to 64 unique explicit gameplay tags from one catalog in sorted wire-index order.
    /// </summary>
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

        /// <summary>Gets the number of explicitly stored tag kinds.</summary>
        public int ExactKindCount => _count;

        /// <summary>Adds a tag when it is not already explicitly stored.</summary>
        /// <param name="tag">The catalog tag to add.</param>
        /// <returns><see langword="true"/> when the tag was added; otherwise <see langword="false"/>.</returns>
        /// <exception cref="ArgumentException"><paramref name="tag"/> is <see cref="GameplayTag.None"/>.</exception>
        /// <exception cref="InvalidOperationException">The container already holds 64 explicit kinds.</exception>
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

        /// <summary>Removes an explicitly stored tag.</summary>
        /// <param name="tag">The catalog tag to remove.</param>
        /// <returns><see langword="true"/> when the tag was removed; otherwise <see langword="false"/>.</returns>
        /// <exception cref="ArgumentException"><paramref name="tag"/> is <see cref="GameplayTag.None"/>.</exception>
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

        /// <summary>Determines whether an explicitly stored tag is the queried tag or one of its descendants.</summary>
        /// <param name="tag">The tag to query.</param>
        /// <returns><see langword="true"/> when a matching explicit tag is stored.</returns>
        public bool Has(GameplayTag tag) => HasCore(tag, exact: false, out _);

        /// <summary>Determines whether the queried tag is explicitly stored.</summary>
        /// <param name="tag">The tag to query.</param>
        /// <returns><see langword="true"/> when the exact tag is stored.</returns>
        public bool HasExact(GameplayTag tag) => HasCore(tag, exact: true, out _);

        /// <summary>Determines whether any hierarchical query tag matches this container.</summary>
        /// <param name="query">The query tags from the same catalog instance.</param>
        /// <returns><see langword="true"/> when any query tag matches; otherwise <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/> belongs to another catalog instance.</exception>
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

        /// <summary>Determines whether every hierarchical query tag matches this container.</summary>
        /// <param name="query">The query tags from the same catalog instance.</param>
        /// <returns><see langword="true"/> when every query tag matches, including an empty query.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/> belongs to another catalog instance.</exception>
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

        /// <summary>Determines whether any exact query tag is explicitly stored.</summary>
        /// <param name="query">The query tags from the same catalog instance.</param>
        /// <returns><see langword="true"/> when any exact query tag matches; otherwise <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/> belongs to another catalog instance.</exception>
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

        /// <summary>Determines whether every exact query tag is explicitly stored.</summary>
        /// <param name="query">The query tags from the same catalog instance.</param>
        /// <returns><see langword="true"/> when every exact query tag matches, including an empty query.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/> belongs to another catalog instance.</exception>
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
