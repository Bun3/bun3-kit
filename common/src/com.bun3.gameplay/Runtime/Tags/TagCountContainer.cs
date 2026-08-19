#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Stores exact tag counts and counts aggregated to all ancestors from a single catalog, as sorted wire indices.</summary>
    public sealed class TagCountContainer : TagQueryContainer
    {
        private const int MaximumExactKinds = 64;
        private const int MaximumDepth = 16;
        private const int MaximumEntries = MaximumExactKinds * MaximumDepth;

        private ushort[] _indices;
        private int[] _exactCounts;
        private int[] _aggregateCounts;
        private int _entryCount;
        private int _exactKindCount;

        internal TagCountContainer(TagCatalog catalog, int expectedExactKinds)
            : base(catalog)
        {
            if ((uint)expectedExactKinds > MaximumExactKinds)
                throw new ArgumentOutOfRangeException(nameof(expectedExactKinds));

            var capacity = expectedExactKinds * MaximumDepth;
            _indices = capacity == 0 ? Array.Empty<ushort>() : new ushort[capacity];
            _exactCounts = capacity == 0 ? Array.Empty<int>() : new int[capacity];
            _aggregateCounts = capacity == 0 ? Array.Empty<int>() : new int[capacity];
        }

        /// <summary>Gets the number of tag kinds whose exact count is greater than zero.</summary>
        public int ExactKindCount => _exactKindCount;

        /// <summary>Copies explicitly stored tags and counts in ascending catalog index order.</summary>
        /// <param name="destination">Buffer receiving the exact tags and counts.</param>
        /// <returns>Number of entries copied.</returns>
        /// <exception cref="ArgumentException">The buffer is smaller than the number of exact tag kinds.</exception>
        public int CopyExactEntries(Span<TagCountEntry> destination)
        {
            if (destination.Length < _exactKindCount)
                throw new ArgumentException(
                    "Buffer is smaller than the exact entry count.", nameof(destination));

            var copied = 0;
            for (var i = 0; i < _entryCount; i++)
            {
                if (_exactCounts[i] == 0)
                    continue;

                destination[copied++] = new TagCountEntry(
                    new GameplayTag(_indices[i]), _exactCounts[i]);
            }

            return copied;
        }

        /// <summary>Gets the number of merge or compaction passes used by the last successful mutation.</summary>
        internal int LastMutationPassCount { get; private set; }

        /// <summary>Gets the number of tags and ancestors collected by the last successful mutation.</summary>
        internal int LastMutationDepth { get; private set; }

        /// <summary>Increments the aggregate count of the tag and all its ancestors.</summary>
        /// <param name="tag">Tag to add.</param>
        /// <param name="count">Positive count to add.</param>
        /// <exception cref="ArgumentException"><paramref name="tag"/> is <see cref="GameplayTag.None"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive, or <paramref name="tag"/> is outside this container's catalog range.</exception>
        /// <exception cref="InvalidOperationException">Exact tag kinds would exceed 64 or aggregate entries would exceed 1,024.</exception>
        /// <exception cref="OverflowException">An exact or aggregate count would exceed <see cref="int.MaxValue"/>.</exception>
        public void Add(GameplayTag tag, int count = 1)
        {
            ValidateMutationTag(tag);
            ValidateMutationCount(count);

            Span<ushort> ancestors = stackalloc ushort[MaximumDepth];
            var ancestorCount = CollectAncestors(tag, ancestors);
            var exactPosition = TagSearch.LowerBound(_indices, _entryCount, tag.Index, out _);
            var hasExact = exactPosition < _entryCount && _indices[exactPosition] == tag.Index && _exactCounts[exactPosition] != 0;
            if (!hasExact && _exactKindCount == MaximumExactKinds)
                throw new InvalidOperationException("TagCountContainer holds at most 64 exact tag kinds.");

            var newEntryCount = _entryCount;
            for (var ancestorPosition = 0; ancestorPosition < ancestorCount; ancestorPosition++)
            {
                var index = TagSearch.LowerBound(_indices, _entryCount, ancestors[ancestorPosition], out _);
                if (index == _entryCount || _indices[index] != ancestors[ancestorPosition])
                {
                    newEntryCount++;
                }
                else
                {
                    _ = checked(_aggregateCounts[index] + count);
                    if (ancestors[ancestorPosition] == tag.Index)
                        _ = checked(_exactCounts[index] + count);
                }
            }

            if (newEntryCount > MaximumEntries)
                throw new InvalidOperationException("TagCountContainer holds at most 1,024 aggregate entries.");
            EnsureCapacity(newEntryCount);

            MergeAddedCounts(tag.Index, count, ancestors, ancestorCount, newEntryCount);
            _entryCount = newEntryCount;
            if (!hasExact)
                _exactKindCount++;
            LastMutationDepth = ancestorCount;
            LastMutationPassCount = 1;
        }

        /// <summary>Subtracts up to the stored amount from the aggregate counts of the tag and all its ancestors.</summary>
        /// <param name="tag">Tag to remove.</param>
        /// <param name="count">Positive count requested to remove.</param>
        /// <returns>Count actually removed.</returns>
        /// <exception cref="ArgumentException"><paramref name="tag"/> is <see cref="GameplayTag.None"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive, or <paramref name="tag"/> is outside this container's catalog range.</exception>
        public int Remove(GameplayTag tag, int count = 1)
        {
            ValidateMutationTag(tag);
            ValidateMutationCount(count);

            var exactPosition = TagSearch.LowerBound(_indices, _entryCount, tag.Index, out _);
            if (exactPosition == _entryCount || _indices[exactPosition] != tag.Index || _exactCounts[exactPosition] == 0)
                return 0;

            var removed = Math.Min(count, _exactCounts[exactPosition]);
            var removesExactKind = removed == _exactCounts[exactPosition];
            Span<ushort> ancestors = stackalloc ushort[MaximumDepth];
            var ancestorCount = CollectAncestors(tag, ancestors);
            ValidateRemoval(ancestors, ancestorCount, removed);

            var destination = 0;
            var ancestorPosition = ancestorCount - 1;
            for (var source = 0; source < _entryCount; source++)
            {
                var index = _indices[source];
                var exact = _exactCounts[source];
                var aggregate = _aggregateCounts[source];
                if (ancestorPosition >= 0 && index == ancestors[ancestorPosition])
                {
                    aggregate -= removed;
                    if (index == tag.Index)
                        exact -= removed;
                    ancestorPosition--;
                }

                if (exact != 0 || aggregate != 0)
                {
                    _indices[destination] = index;
                    _exactCounts[destination] = exact;
                    _aggregateCounts[destination] = aggregate;
                    destination++;
                }
            }

            for (var index = destination; index < _entryCount; index++)
            {
                _indices[index] = 0;
                _exactCounts[index] = 0;
                _aggregateCounts[index] = 0;
            }

            _entryCount = destination;
            if (removesExactKind)
                _exactKindCount--;
            LastMutationDepth = ancestorCount;
            LastMutationPassCount = 1;
            return removed;
        }

        /// <summary>Gets the count explicitly stored on the tag itself.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <returns>The count explicitly stored on the tag itself.</returns>
        public int ExactCount(GameplayTag tag)
        {
            GetCountsCore(tag, out var exact, out _, out _);
            return exact;
        }

        /// <summary>Gets the aggregate count stored on the tag and its descendants.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <returns>The aggregate count of the tag and its descendants.</returns>
        public int Count(GameplayTag tag)
        {
            GetCountsCore(tag, out _, out var aggregate, out _);
            return aggregate;
        }

        /// <summary>Checks whether the tag itself has an explicitly stored count.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <returns><see langword="true"/> if an explicitly stored count exists.</returns>
        public override bool HasExact(GameplayTag tag)
        {
            GetCountsCore(tag, out var exact, out _, out _);
            return exact != 0;
        }

        /// <summary>Checks whether the tag or any of its descendants has a stored count.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <returns><see langword="true"/> if an aggregate count exists.</returns>
        public override bool Has(GameplayTag tag)
        {
            GetCountsCore(tag, out _, out var aggregate, out _);
            return aggregate != 0;
        }

        internal void GetCountsCore(GameplayTag tag, out int exact, out int aggregate, out int comparisons)
        {
            if (!tag.IsValid)
            {
                exact = 0;
                aggregate = 0;
                comparisons = 0;
                return;
            }

            var index = TagSearch.LowerBound(_indices, _entryCount, tag.Index, out comparisons);
            if (index == _entryCount || _indices[index] != tag.Index)
            {
                exact = 0;
                aggregate = 0;
                return;
            }

            exact = _exactCounts[index];
            aggregate = _aggregateCounts[index];
        }

        private int CollectAncestors(GameplayTag tag, Span<ushort> ancestors)
        {
            var count = 0;
            var current = tag;
            while (current.IsValid)
            {
                ancestors[count++] = current.Index;
                current = Catalog.GetParent(current);
            }

            return count;
        }

        private void MergeAddedCounts(ushort tagIndex, int count, Span<ushort> ancestors, int ancestorCount, int newEntryCount)
        {
            var source = _entryCount - 1;
            var ancestorPosition = 0;
            var destination = newEntryCount - 1;
            while (source >= 0 || ancestorPosition < ancestorCount)
            {
                if (ancestorPosition == ancestorCount || (source >= 0 && _indices[source] > ancestors[ancestorPosition]))
                {
                    _indices[destination] = _indices[source];
                    _exactCounts[destination] = _exactCounts[source];
                    _aggregateCounts[destination] = _aggregateCounts[source];
                    source--;
                }
                else if (source < 0 || _indices[source] < ancestors[ancestorPosition])
                {
                    _indices[destination] = ancestors[ancestorPosition];
                    _exactCounts[destination] = ancestors[ancestorPosition] == tagIndex ? count : 0;
                    _aggregateCounts[destination] = count;
                    ancestorPosition++;
                }
                else
                {
                    _indices[destination] = _indices[source];
                    _exactCounts[destination] = _exactCounts[source] + (ancestors[ancestorPosition] == tagIndex ? count : 0);
                    _aggregateCounts[destination] = _aggregateCounts[source] + count;
                    source--;
                    ancestorPosition++;
                }

                destination--;
            }
        }

        private void ValidateRemoval(Span<ushort> ancestors, int ancestorCount, int removed)
        {
            for (var ancestorPosition = 0; ancestorPosition < ancestorCount; ancestorPosition++)
            {
                var index = TagSearch.LowerBound(_indices, _entryCount, ancestors[ancestorPosition], out _);
                if (index == _entryCount || _indices[index] != ancestors[ancestorPosition] || _aggregateCounts[index] < removed)
                    throw new InvalidOperationException("TagCountContainer aggregate state is corrupted.");
            }
        }

        private static void ValidateMutationCount(int count)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _indices.Length)
                return;

            var capacity = _indices.Length == 0 ? 4 : _indices.Length * 2;
            if (capacity < required)
                capacity = required;
            if (capacity > MaximumEntries)
                capacity = MaximumEntries;

            var indices = new ushort[capacity];
            var exactCounts = new int[capacity];
            var aggregateCounts = new int[capacity];
            Array.Copy(_indices, indices, _entryCount);
            Array.Copy(_exactCounts, exactCounts, _entryCount);
            Array.Copy(_aggregateCounts, aggregateCounts, _entryCount);
            _indices = indices;
            _exactCounts = exactCounts;
            _aggregateCounts = aggregateCounts;
        }
    }
}
