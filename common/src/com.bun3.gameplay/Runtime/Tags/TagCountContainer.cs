#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>하나의 카탈로그에서 명시적 태그 수와 모든 조상에 누적된 태그 수를 정렬된 와이어 인덱스로 저장합니다.</summary>
    public sealed class TagCountContainer
    {
        private const int MaximumExactKinds = 64;
        private const int MaximumDepth = 16;
        private const int MaximumEntries = MaximumExactKinds * MaximumDepth;

        private readonly TagCatalog _catalog;
        private ushort[] _indices;
        private int[] _exactCounts;
        private int[] _aggregateCounts;
        private int _entryCount;
        private int _exactKindCount;

        internal TagCountContainer(TagCatalog catalog, int expectedExactKinds)
        {
            if ((uint)expectedExactKinds > MaximumExactKinds)
                throw new ArgumentOutOfRangeException(nameof(expectedExactKinds));

            _catalog = catalog;
            var capacity = expectedExactKinds * MaximumDepth;
            _indices = capacity == 0 ? Array.Empty<ushort>() : new ushort[capacity];
            _exactCounts = capacity == 0 ? Array.Empty<int>() : new int[capacity];
            _aggregateCounts = capacity == 0 ? Array.Empty<int>() : new int[capacity];
        }

        /// <summary>명시적 수가 0보다 큰 태그 종류 수를 가져옵니다.</summary>
        public int ExactKindCount => _exactKindCount;

        /// <summary>마지막으로 성공한 변경이 사용한 병합 또는 압축 통과 수를 가져옵니다.</summary>
        internal int LastMutationPassCount { get; private set; }

        /// <summary>마지막으로 성공한 변경에서 수집한 태그와 조상 수를 가져옵니다.</summary>
        internal int LastMutationDepth { get; private set; }

        /// <summary>태그 자신과 모든 조상의 누적 수를 늘립니다.</summary>
        /// <param name="tag">추가할 태그입니다.</param>
        /// <param name="count">추가할 양의 수입니다.</param>
        /// <exception cref="ArgumentException"><paramref name="tag"/>가 <see cref="GameplayTag.None"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/>가 양수가 아닌 경우입니다.</exception>
        /// <exception cref="InvalidOperationException">명시적 태그 종류가 64개를 초과하거나 누적 항목이 1,024개를 초과하는 경우입니다.</exception>
        /// <exception cref="OverflowException">명시적 또는 누적 수가 <see cref="int.MaxValue"/>를 초과하는 경우입니다.</exception>
        public void Add(GameplayTag tag, int count = 1)
        {
            ValidateMutation(tag, count);

            Span<ushort> ancestors = stackalloc ushort[MaximumDepth];
            var ancestorCount = CollectAncestors(tag, ancestors);
            var exactPosition = TagSearch.LowerBound(_indices, _entryCount, tag.Index, out _);
            var hasExact = exactPosition < _entryCount && _indices[exactPosition] == tag.Index && _exactCounts[exactPosition] != 0;
            if (!hasExact && _exactKindCount == MaximumExactKinds)
                throw new InvalidOperationException("A TagCountContainer cannot hold more than 64 explicit kinds.");

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
                throw new InvalidOperationException("A TagCountContainer cannot hold more than 1,024 aggregate entries.");
            EnsureCapacity(newEntryCount);

            MergeAddedCounts(tag.Index, count, ancestors, ancestorCount, newEntryCount);
            _entryCount = newEntryCount;
            if (!hasExact)
                _exactKindCount++;
            LastMutationDepth = ancestorCount;
            LastMutationPassCount = 1;
        }

        /// <summary>태그 자신과 모든 조상의 누적 수에서 실제로 저장된 만큼을 뺍니다.</summary>
        /// <param name="tag">제거할 태그입니다.</param>
        /// <param name="count">제거를 요청할 양의 수입니다.</param>
        /// <returns>실제로 제거한 수입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="tag"/>가 <see cref="GameplayTag.None"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/>가 양수가 아닌 경우입니다.</exception>
        public int Remove(GameplayTag tag, int count = 1)
        {
            ValidateMutation(tag, count);

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

        /// <summary>태그 자신에게 명시적으로 저장된 수를 가져옵니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>태그 자신에게 명시적으로 저장된 수입니다.</returns>
        public int ExactCount(GameplayTag tag)
        {
            GetCountsCore(tag, out var exact, out _, out _);
            return exact;
        }

        /// <summary>태그 자신과 그 자손에게 저장된 누적 수를 가져옵니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>태그 자신과 그 자손의 누적 수입니다.</returns>
        public int Count(GameplayTag tag)
        {
            GetCountsCore(tag, out _, out var aggregate, out _);
            return aggregate;
        }

        /// <summary>태그 자신에게 명시적으로 저장된 수가 있는지 확인합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>명시적으로 저장된 수가 있으면 <see langword="true"/>입니다.</returns>
        public bool HasExact(GameplayTag tag)
        {
            GetCountsCore(tag, out var exact, out _, out _);
            return exact != 0;
        }

        /// <summary>태그 자신 또는 자손에게 저장된 수가 있는지 확인합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>누적 수가 있으면 <see langword="true"/>입니다.</returns>
        public bool Has(GameplayTag tag)
        {
            GetCountsCore(tag, out _, out var aggregate, out _);
            return aggregate != 0;
        }

        /// <summary>계층형 조회 태그 중 하나라도 누적 수를 가지는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그에서 만든 조회 태그 컨테이너입니다.</param>
        /// <returns>조회 태그 중 하나라도 누적 수를 가지면 <see langword="true"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그에서 만들어진 경우입니다.</exception>
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

        /// <summary>계층형 조회 태그가 모두 누적 수를 가지는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그에서 만든 조회 태그 컨테이너입니다.</param>
        /// <returns>빈 조회를 포함해 모든 조회 태그가 누적 수를 가지면 <see langword="true"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그에서 만들어진 경우입니다.</exception>
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

        /// <summary>정확한 조회 태그 중 하나라도 명시적 수를 가지는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그에서 만든 조회 태그 컨테이너입니다.</param>
        /// <returns>조회 태그 중 하나라도 명시적 수를 가지면 <see langword="true"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그에서 만들어진 경우입니다.</exception>
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

        /// <summary>정확한 조회 태그가 모두 명시적 수를 가지는지 확인합니다.</summary>
        /// <param name="query">같은 카탈로그에서 만든 조회 태그 컨테이너입니다.</param>
        /// <returns>빈 조회를 포함해 모든 정확한 조회 태그가 명시적 수를 가지면 <see langword="true"/>입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/>가 <see langword="null"/>인 경우입니다.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/>가 다른 카탈로그에서 만들어진 경우입니다.</exception>
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
                current = _catalog.GetParent(current);
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
                    throw new InvalidOperationException("TagCountContainer aggregate state is invalid.");
            }
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

        private static void ValidateMutation(GameplayTag tag, int count)
        {
            if (!tag.IsValid)
                throw new ArgumentException("GameplayTag.None cannot be stored in a TagCountContainer.", nameof(tag));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));
        }

        private void ValidateQueryCatalog(TagContainer query)
        {
            if (query is null)
                throw new ArgumentNullException(nameof(query));
            if (!ReferenceEquals(_catalog, query.Catalog))
                throw new ArgumentException("TagCountContainer queries require the same TagCatalog instance.", nameof(query));
        }
    }
}
