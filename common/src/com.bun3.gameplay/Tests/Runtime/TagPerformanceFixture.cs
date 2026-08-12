#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests
{
    internal enum TagContainerKind
    {
        TagContainer,
        TagCountContainer
    }

    internal enum TagQueryKind
    {
        ExactHit,
        ParentHit,
        Miss
    }

    internal enum TagMutationKind
    {
        AddRemove,
        ReadWriteMixed
    }

    internal readonly struct TagPerformanceResult
    {
        internal TagPerformanceResult(
            string backend, int catalogSize, int exactKinds, int depth,
            TagContainerKind containerKind, TagQueryKind queryKind,
            long newP50Ticks, long newP95Ticks, long newP99Ticks,
            long legacyP50Ticks, long legacyP95Ticks, long legacyP99Ticks,
            int semanticChecksum, long allocationCount)
        {
            Backend = backend; CatalogSize = catalogSize; ExactKinds = exactKinds; Depth = depth;
            ContainerKind = containerKind; QueryKind = queryKind;
            NewP50Ticks = newP50Ticks; NewP95Ticks = newP95Ticks; NewP99Ticks = newP99Ticks;
            LegacyP50Ticks = legacyP50Ticks; LegacyP95Ticks = legacyP95Ticks;
            LegacyP99Ticks = legacyP99Ticks; SemanticChecksum = semanticChecksum;
            AllocationCount = allocationCount;
        }

        internal string Backend { get; }
        internal int CatalogSize { get; }
        internal int ExactKinds { get; }
        internal int Depth { get; }
        internal TagContainerKind ContainerKind { get; }
        internal TagQueryKind QueryKind { get; }
        internal long NewP50Ticks { get; }
        internal long NewP95Ticks { get; }
        internal long NewP99Ticks { get; }
        internal long LegacyP50Ticks { get; }
        internal long LegacyP95Ticks { get; }
        internal long LegacyP99Ticks { get; }
        internal int SemanticChecksum { get; }
        internal long AllocationCount { get; }

        internal string ToLogLine()
        {
            return $"TAGPERF backend={Backend} N={CatalogSize} M={ExactKinds} D={Depth} " +
                $"container={ContainerKind} kind={QueryKind} " +
                $"new_p50_ticks={NewP50Ticks} new_p95_ticks={NewP95Ticks} new_p99_ticks={NewP99Ticks} " +
                $"legacy_p50_ticks={LegacyP50Ticks} legacy_p95_ticks={LegacyP95Ticks} " +
                $"legacy_p99_ticks={LegacyP99Ticks} semantic_checksum={SemanticChecksum} " +
                $"alloc_count={AllocationCount}";
        }
    }

    internal readonly struct TagMutationPerformanceResult
    {
        internal TagMutationPerformanceResult(
            string backend, int catalogSize, int exactKinds, int depth,
            TagContainerKind containerKind, TagMutationKind mutationKind,
            long newP50Ticks, long newP95Ticks, long newP99Ticks,
            long legacyP50Ticks, long legacyP95Ticks, long legacyP99Ticks,
            int semanticChecksum, long allocationCount)
        {
            Backend = backend; CatalogSize = catalogSize; ExactKinds = exactKinds; Depth = depth;
            ContainerKind = containerKind; MutationKind = mutationKind;
            NewP50Ticks = newP50Ticks; NewP95Ticks = newP95Ticks; NewP99Ticks = newP99Ticks;
            LegacyP50Ticks = legacyP50Ticks; LegacyP95Ticks = legacyP95Ticks;
            LegacyP99Ticks = legacyP99Ticks; SemanticChecksum = semanticChecksum;
            AllocationCount = allocationCount;
        }

        internal string Backend { get; }
        internal int CatalogSize { get; }
        internal int ExactKinds { get; }
        internal int Depth { get; }
        internal TagContainerKind ContainerKind { get; }
        internal TagMutationKind MutationKind { get; }
        internal long NewP50Ticks { get; }
        internal long NewP95Ticks { get; }
        internal long NewP99Ticks { get; }
        internal long LegacyP50Ticks { get; }
        internal long LegacyP95Ticks { get; }
        internal long LegacyP99Ticks { get; }
        internal int SemanticChecksum { get; }
        internal long AllocationCount { get; }

        internal string ToLogLine()
        {
            return $"TAGMUT backend={Backend} N={CatalogSize} M={ExactKinds} D={Depth} " +
                $"container={ContainerKind} kind={MutationKind} " +
                $"new_p50_ticks={NewP50Ticks} new_p95_ticks={NewP95Ticks} new_p99_ticks={NewP99Ticks} " +
                $"legacy_p50_ticks={LegacyP50Ticks} legacy_p95_ticks={LegacyP95Ticks} " +
                $"legacy_p99_ticks={LegacyP99Ticks} semantic_checksum={SemanticChecksum} " +
                $"alloc_count={AllocationCount}";
        }
    }

    internal sealed class TagRuntimeFixture
    {
        private readonly TagContainerKind _containerKind;
        private readonly TagContainer? _tags;
        private readonly TagCountContainer? _counts;
        private readonly LegacyTagQueryBaseline _legacy;
        private readonly GameplayTag[] _exactQueries;
        private readonly GameplayTag[] _parentQueries;
        private readonly GameplayTag[] _missQueries;
        private readonly int[] _newQueryPositions = new int[3];
        private readonly int[] _legacyQueryPositions = new int[3];
        private readonly bool _startEmpty;
        private int _newMutationPosition;
        private int _legacyMutationPosition;
        private bool _newRemoveNext = true;
        private bool _legacyRemoveNext = true;

        private TagRuntimeFixture(
            TagContainerKind containerKind,
            TagContainer? tags,
            TagCountContainer? counts,
            LegacyTagQueryBaseline legacy,
            GameplayTag[] exactQueries,
            GameplayTag[] parentQueries,
            GameplayTag[] missQueries,
            bool startEmpty)
        {
            _containerKind = containerKind;
            _tags = tags;
            _counts = counts;
            _legacy = legacy;
            _exactQueries = exactQueries;
            _parentQueries = parentQueries;
            _missQueries = missQueries;
            _startEmpty = startEmpty;
        }

        internal static TagRuntimeFixture Create(
            int catalogSize,
            int exactKinds,
            int depth,
            TagContainerKind containerKind,
            bool startEmpty = false)
        {
            if (catalogSize < exactKinds * depth)
                throw new ArgumentOutOfRangeException(nameof(catalogSize));

            var json = BuildCatalogJson(catalogSize, exactKinds, depth);
            using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(json));
            var catalog = TagCatalog.Load(stream);
            var exact = new GameplayTag[exactKinds];
            var parents = new GameplayTag[exactKinds];
            var misses = new GameplayTag[exactKinds];
            for (var index = 0; index < exactKinds; index++)
            {
                exact[index] = catalog.GetRequired(BuildChainLeaf(index, depth));
                parents[index] = catalog.GetRequired("B" + index);
                misses[index] = catalog.GetRequired("F" + index);
            }

            TagContainer? tags = null;
            TagCountContainer? counts = null;
            if (containerKind == TagContainerKind.TagContainer)
                tags = catalog.CreateContainer(exactKinds);
            else
                counts = catalog.CreateCountContainer(exactKinds);

            var legacy = new LegacyTagQueryBaseline(catalog, exactKinds);
            if (!startEmpty)
            {
                for (var index = 0; index < exact.Length; index++)
                {
                    if (tags is not null)
                        tags.Add(exact[index]);
                    else
                        counts!.Add(exact[index]);
                    legacy.Add(exact[index]);
                }
            }

            return new TagRuntimeFixture(
                containerKind, tags, counts, legacy, exact, parents, misses, startEmpty);
        }

        internal void WarmUp()
        {
            for (var round = 0; round < 3; round++)
            {
                RunNewQueries(64, TagQueryKind.ExactHit);
                RunLegacyQueries(64, TagQueryKind.ExactHit);
                RunLegacyQueries(64, TagQueryKind.ParentHit);
                RunNewQueries(64, TagQueryKind.ParentHit);
                RunNewQueries(64, TagQueryKind.Miss);
                RunLegacyQueries(64, TagQueryKind.Miss);
            }
        }

        internal void WarmUpMutation()
        {
            if (_startEmpty)
            {
                RunReservedAddRemoveCycles(16);
                RunLegacyAddRemoveCycles(16);
            }
            else
            {
                RunNewReadWriteMixed(100);
                RunLegacyReadWriteMixed(100);
            }
        }

        internal int RunNewQueries(int iterations, TagQueryKind kind)
        {
            var queries = GetQueries(kind);
            var position = _newQueryPositions[(int)kind];
            var checksum = 0;
            if (_containerKind == TagContainerKind.TagContainer)
            {
                var tags = _tags!;
                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    if (tags.Has(queries[position])) checksum++;
                    if (++position == queries.Length) position = 0;
                }
            }
            else
            {
                var counts = _counts!;
                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    if (counts.Has(queries[position])) checksum++;
                    if (++position == queries.Length) position = 0;
                }
            }

            _newQueryPositions[(int)kind] = position;
            return checksum;
        }

        internal int RunLegacyQueries(int iterations, TagQueryKind kind)
        {
            var queries = GetQueries(kind);
            var position = _legacyQueryPositions[(int)kind];
            var checksum = 0;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                if (_legacy.Has(queries[position])) checksum++;
                if (++position == queries.Length) position = 0;
            }

            _legacyQueryPositions[(int)kind] = position;
            return checksum;
        }

        internal int RunReservedAddRemoveCycles(int operations)
        {
            var position = _newMutationPosition;
            var checksum = 0;
            if (_containerKind == TagContainerKind.TagContainer)
            {
                var tags = _tags!;
                for (var operation = 0; operation < operations; operation++)
                {
                    if (tags.Add(_exactQueries[position])) checksum++;
                    if (tags.Remove(_exactQueries[position])) checksum += 2;
                    if (++position == _exactQueries.Length) position = 0;
                }
            }
            else
            {
                var counts = _counts!;
                for (var operation = 0; operation < operations; operation++)
                {
                    counts.Add(_exactQueries[position]);
                    checksum += counts.Remove(_exactQueries[position]) * 3;
                    if (++position == _exactQueries.Length) position = 0;
                }
            }

            _newMutationPosition = position;
            var exactKindCount = _containerKind == TagContainerKind.TagContainer
                ? _tags!.ExactKindCount
                : _counts!.ExactKindCount;
            return checked(checksum * 31 + exactKindCount);
        }

        internal int RunLegacyAddRemoveCycles(int operations)
        {
            var position = _legacyMutationPosition;
            var checksum = 0;
            for (var operation = 0; operation < operations; operation++)
            {
                if (_containerKind == TagContainerKind.TagContainer)
                {
                    if (_legacy.Add(_exactQueries[position])) checksum++;
                    if (_legacy.Remove(_exactQueries[position]) != 0) checksum += 2;
                }
                else
                {
                    _legacy.Add(_exactQueries[position]);
                    checksum += _legacy.Remove(_exactQueries[position]) * 3;
                }
                if (++position == _exactQueries.Length) position = 0;
            }

            _legacyMutationPosition = position;
            return checked(checksum * 31 + _legacy.ExactKindCount);
        }

        internal int RunNewReadWriteMixed(int operations)
        {
            var position = _newMutationPosition;
            var removeNext = _newRemoveNext;
            var checksum = 0;
            var groups = operations / 10;
            if (_containerKind == TagContainerKind.TagContainer)
            {
                var tags = _tags!;
                for (var group = 0; group < groups; group++)
                {
                    for (var read = 0; read < 9; read++)
                    {
                        if (tags.Has(_parentQueries[position])) checksum++;
                        if (++position == _parentQueries.Length) position = 0;
                    }

                    if (removeNext)
                        tags.Remove(_exactQueries[0]);
                    else
                        tags.Add(_exactQueries[0]);
                    removeNext = !removeNext;
                    checksum += tags.ExactKindCount;
                }
            }
            else
            {
                var counts = _counts!;
                for (var group = 0; group < groups; group++)
                {
                    for (var read = 0; read < 9; read++)
                    {
                        if (counts.Has(_parentQueries[position])) checksum++;
                        if (++position == _parentQueries.Length) position = 0;
                    }

                    if (removeNext)
                        counts.Remove(_exactQueries[0]);
                    else
                        counts.Add(_exactQueries[0]);
                    removeNext = !removeNext;
                    checksum += counts.ExactKindCount;
                }
            }

            _newMutationPosition = position;
            _newRemoveNext = removeNext;
            return checksum;
        }

        internal int RunLegacyReadWriteMixed(int operations)
        {
            var position = _legacyMutationPosition;
            var removeNext = _legacyRemoveNext;
            var checksum = 0;
            var groups = operations / 10;
            for (var group = 0; group < groups; group++)
            {
                for (var read = 0; read < 9; read++)
                {
                    if (_legacy.Has(_parentQueries[position])) checksum++;
                    if (++position == _parentQueries.Length) position = 0;
                }

                if (removeNext)
                    _legacy.Remove(_exactQueries[0]);
                else
                    _legacy.Add(_exactQueries[0]);
                removeNext = !removeNext;
                checksum += _legacy.ExactKindCount;
            }

            _legacyMutationPosition = position;
            _legacyRemoveNext = removeNext;
            return checksum;
        }

        private GameplayTag[] GetQueries(TagQueryKind kind)
        {
            if (kind == TagQueryKind.ExactHit) return _exactQueries;
            return kind == TagQueryKind.ParentHit ? _parentQueries : _missQueries;
        }

        private static string BuildCatalogJson(int catalogSize, int exactKinds, int depth)
        {
            var json = new StringBuilder(catalogSize * 24);
            json.Append("{\"schemaVersion\":1,\"tags\":[");
            var written = 0;
            for (var index = 0; index < exactKinds; index++)
            {
                if (written++ != 0) json.Append(',');
                json.Append("{\"name\":\"").Append(BuildChainLeaf(index, depth)).Append("\"}");
            }

            for (var index = 0; index < catalogSize - exactKinds * depth; index++)
            {
                if (written++ != 0) json.Append(',');
                json.Append("{\"name\":\"F").Append(index).Append("\"}");
            }

            return json.Append("]}").ToString();
        }

        private static string BuildChainLeaf(int chain, int depth)
        {
            var path = new StringBuilder().Append('B').Append(chain);
            for (var level = 1; level < depth; level++)
                path.Append(".L").Append(level);
            return path.ToString();
        }
    }

    internal sealed class LegacyTagQueryBaseline
    {
        private readonly TagCatalog _catalog;
        private readonly Dictionary<ushort, int> _exactCounts;

        internal LegacyTagQueryBaseline(TagCatalog catalog, int capacity)
        {
            _catalog = catalog;
            _exactCounts = new Dictionary<ushort, int>(capacity);
        }

        internal int ExactKindCount => _exactCounts.Count;

        internal bool Add(GameplayTag tag)
        {
            if (_exactCounts.TryGetValue(tag.Index, out var count))
            {
                _exactCounts[tag.Index] = count + 1;
                return false;
            }
            else
                _exactCounts.Add(tag.Index, 1);
            return true;
        }

        internal int Remove(GameplayTag tag)
        {
            if (!_exactCounts.TryGetValue(tag.Index, out var count)) return 0;
            if (count == 1)
                _exactCounts.Remove(tag.Index);
            else
                _exactCounts[tag.Index] = count - 1;
            return 1;
        }

        internal bool Has(GameplayTag query)
        {
            foreach (var pair in _exactCounts)
            {
                if (pair.Value == 0) continue;
                var current = _catalog.GetRequiredByIndex(pair.Key);
                while (current.IsValid)
                {
                    if (current == query) return true;
                    current = _catalog.GetParent(current);
                }
            }

            return false;
        }
    }

    internal static class TagPerformanceFixture
    {
        private const int LatencyBlockSize = 16;
        private const int LatencySampleCount = 1_001;
        private static readonly int[] CatalogSizes = { 5_000, 50_000 };
        private static readonly int[] ExactKindCounts = { 8, 32, 64 };
        private static readonly int[] Depths = { 1, 4, 8, 16 };
        private static readonly TagContainerKind[] ContainerKinds =
            { TagContainerKind.TagContainer, TagContainerKind.TagCountContainer };
        private static readonly TagQueryKind[] QueryKinds =
            { TagQueryKind.ExactHit, TagQueryKind.ParentHit, TagQueryKind.Miss };
        private static readonly TagMutationKind[] MutationKinds =
            { TagMutationKind.AddRemove, TagMutationKind.ReadWriteMixed };

        internal static TagPerformanceResult[] MeasureMatrix(string backendName)
        {
            var results = new TagPerformanceResult[144];
            var resultIndex = 0;
            foreach (var catalogSize in CatalogSizes)
            foreach (var exactKinds in ExactKindCounts)
            foreach (var depth in Depths)
            foreach (var containerKind in ContainerKinds)
            foreach (var queryKind in QueryKinds)
            {
                var fixture = TagRuntimeFixture.Create(catalogSize, exactKinds, depth, containerKind);
                fixture.WarmUp();
                var newWorkload = new QueryWorkload(fixture, 100_000, queryKind, useLegacy: false);
                var legacyWorkload = new QueryWorkload(fixture, 100_000, queryKind, useLegacy: true);
                newWorkload.Run();
                var semanticChecksum = newWorkload.Checksum;
                legacyWorkload.Run();
                var expectedChecksum = queryKind == TagQueryKind.Miss ? 0 : 100_000;
                Assert.That(semanticChecksum, Is.EqualTo(expectedChecksum));
                Assert.That(legacyWorkload.Checksum, Is.EqualTo(semanticChecksum));
                var allocationCount = MeasureAllocation(newWorkload.Run);
                legacyWorkload.Run();
                Assert.That(newWorkload.Checksum, Is.EqualTo(expectedChecksum));
                Assert.That(legacyWorkload.Checksum, Is.EqualTo(expectedChecksum));
                Assert.That(allocationCount, Is.Zero);

                var newBatchTicks = MeasureQueryBatch(newWorkload);
                var legacyBatchTicks = MeasureQueryBatch(legacyWorkload);
                Assert.That(newWorkload.Checksum, Is.EqualTo(legacyWorkload.Checksum));
                Assert.That(newBatchTicks, Is.LessThanOrEqualTo(legacyBatchTicks));

                var newTicks = new long[LatencySampleCount];
                var legacyTicks = new long[LatencySampleCount];
                for (var sample = 0; sample < LatencySampleCount; sample++)
                {
                    if ((sample & 1) == 0)
                    {
                        newTicks[sample] = MeasureNewLatency(fixture, queryKind);
                        legacyTicks[sample] = MeasureLegacyLatency(fixture, queryKind);
                    }
                    else
                    {
                        legacyTicks[sample] = MeasureLegacyLatency(fixture, queryKind);
                        newTicks[sample] = MeasureNewLatency(fixture, queryKind);
                    }
                }

                Array.Sort(newTicks);
                Array.Sort(legacyTicks);
                results[resultIndex++] = new TagPerformanceResult(
                    backendName, catalogSize, exactKinds, depth, containerKind, queryKind,
                    newTicks[500], newTicks[950], newTicks[990],
                    legacyTicks[500], legacyTicks[950], legacyTicks[990],
                    semanticChecksum, allocationCount);
            }

            return results;
        }

        internal static TagMutationPerformanceResult[] MeasureMutationMatrix(string backendName)
        {
            var results = new TagMutationPerformanceResult[96];
            var resultIndex = 0;
            foreach (var catalogSize in CatalogSizes)
            foreach (var exactKinds in ExactKindCounts)
            foreach (var depth in Depths)
            foreach (var containerKind in ContainerKinds)
            foreach (var mutationKind in MutationKinds)
            {
                var fixture = TagRuntimeFixture.Create(
                    catalogSize, exactKinds, depth, containerKind,
                    startEmpty: mutationKind == TagMutationKind.AddRemove);
                fixture.WarmUpMutation();
                var newWorkload = new MutationWorkload(fixture, 1_000, mutationKind, useLegacy: false);
                var legacyWorkload = new MutationWorkload(fixture, 1_000, mutationKind, useLegacy: true);
                newWorkload.Run();
                var semanticChecksum = newWorkload.Checksum;
                legacyWorkload.Run();
                var expectedChecksum = GetExpectedMutationChecksum(mutationKind, exactKinds);
                Assert.That(semanticChecksum, Is.EqualTo(expectedChecksum));
                Assert.That(legacyWorkload.Checksum, Is.EqualTo(semanticChecksum));
                var allocationCount = MeasureAllocation(newWorkload.Run);
                legacyWorkload.Run();
                Assert.That(allocationCount, Is.Zero);
                Assert.That(newWorkload.Checksum, Is.EqualTo(legacyWorkload.Checksum));

                var newTicks = new long[101];
                var legacyTicks = new long[101];
                for (var sample = 0; sample < newTicks.Length; sample++)
                {
                    if ((sample & 1) == 0)
                    {
                        newTicks[sample] = MeasureMutationBatch(newWorkload);
                        legacyTicks[sample] = MeasureMutationBatch(legacyWorkload);
                    }
                    else
                    {
                        legacyTicks[sample] = MeasureMutationBatch(legacyWorkload);
                        newTicks[sample] = MeasureMutationBatch(newWorkload);
                    }
                    Assert.That(newWorkload.Checksum, Is.EqualTo(legacyWorkload.Checksum));
                }

                if (mutationKind == TagMutationKind.ReadWriteMixed)
                {
                    var newBatch = new MutationWorkload(fixture, 100_000, mutationKind, useLegacy: false);
                    var legacyBatch = new MutationWorkload(fixture, 100_000, mutationKind, useLegacy: true);
                    var newBatchTicks = MeasureMutationBatch(newBatch);
                    var legacyBatchTicks = MeasureMutationBatch(legacyBatch);
                    Assert.That(newBatch.Checksum, Is.EqualTo(legacyBatch.Checksum));
                    Assert.That(newBatchTicks, Is.LessThanOrEqualTo(legacyBatchTicks));
                }

                Array.Sort(newTicks);
                Array.Sort(legacyTicks);
                results[resultIndex++] = new TagMutationPerformanceResult(
                    backendName, catalogSize, exactKinds, depth, containerKind, mutationKind,
                    newTicks[50], newTicks[95], newTicks[99],
                    legacyTicks[50], legacyTicks[95], legacyTicks[99],
                    semanticChecksum, allocationCount);
            }

            return results;
        }

        private static int GetExpectedMutationChecksum(TagMutationKind mutationKind, int exactKinds)
        {
            if (mutationKind == TagMutationKind.AddRemove) return 93_000;
            if (exactKinds == 8) return 1_600;
            return exactKinds == 32 ? 4_038 : 7_244;
        }

        private static long MeasureAllocation(Action workload)
        {
#if UNITY_5_3_OR_NEWER
            var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            recorder.enabled = false;
#if !UNITY_WEBGL
            recorder.FilterToCurrentThread();
#endif
            recorder.enabled = true;
            try { workload(); }
            finally
            {
                recorder.enabled = false;
#if !UNITY_WEBGL
                recorder.CollectFromAllThreads();
#endif
            }
            return recorder.sampleBlockCount;
#else
            var before = GC.GetAllocatedBytesForCurrentThread();
            workload();
            return GC.GetAllocatedBytesForCurrentThread() - before;
#endif
        }

        private static long MeasureQueryBatch(QueryWorkload workload)
        {
            var before = Stopwatch.GetTimestamp();
            workload.Run();
            return Stopwatch.GetTimestamp() - before;
        }

        private static long MeasureMutationBatch(MutationWorkload workload)
        {
            var before = Stopwatch.GetTimestamp();
            workload.Run();
            return Stopwatch.GetTimestamp() - before;
        }

        private static long MeasureNewLatency(TagRuntimeFixture fixture, TagQueryKind queryKind)
        {
            var before = Stopwatch.GetTimestamp();
            fixture.RunNewQueries(LatencyBlockSize, queryKind);
            return Stopwatch.GetTimestamp() - before;
        }

        private static long MeasureLegacyLatency(TagRuntimeFixture fixture, TagQueryKind queryKind)
        {
            var before = Stopwatch.GetTimestamp();
            fixture.RunLegacyQueries(LatencyBlockSize, queryKind);
            return Stopwatch.GetTimestamp() - before;
        }

        private sealed class QueryWorkload
        {
            private readonly TagRuntimeFixture _fixture;
            private readonly int _iterations;
            private readonly TagQueryKind _kind;
            private readonly bool _useLegacy;

            internal QueryWorkload(TagRuntimeFixture fixture, int iterations, TagQueryKind kind, bool useLegacy)
            {
                _fixture = fixture;
                _iterations = iterations;
                _kind = kind;
                _useLegacy = useLegacy;
            }

            internal int Checksum { get; private set; }

            internal void Run()
            {
                Checksum = _useLegacy
                    ? _fixture.RunLegacyQueries(_iterations, _kind)
                    : _fixture.RunNewQueries(_iterations, _kind);
            }
        }

        private sealed class MutationWorkload
        {
            private readonly TagRuntimeFixture _fixture;
            private readonly int _operations;
            private readonly TagMutationKind _kind;
            private readonly bool _useLegacy;

            internal MutationWorkload(
                TagRuntimeFixture fixture, int operations, TagMutationKind kind, bool useLegacy)
            {
                _fixture = fixture;
                _operations = operations;
                _kind = kind;
                _useLegacy = useLegacy;
            }

            internal int Checksum { get; private set; }

            internal void Run()
            {
                if (_kind == TagMutationKind.ReadWriteMixed)
                {
                    Checksum = _useLegacy
                        ? _fixture.RunLegacyReadWriteMixed(_operations)
                        : _fixture.RunNewReadWriteMixed(_operations);
                    return;
                }

                Checksum = _useLegacy
                    ? _fixture.RunLegacyAddRemoveCycles(_operations)
                    : _fixture.RunReservedAddRemoveCycles(_operations);
            }
        }
    }
}
