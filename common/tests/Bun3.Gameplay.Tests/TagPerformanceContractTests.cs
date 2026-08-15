using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagPerformanceContractTests
{
    [Test]
    public void Lower_bound_never_exceeds_seven_or_eleven_index_comparisons()
    {
        AssertComparisonBound(64, 7);
        AssertComparisonBound(1_024, 11);
    }

    [Test]
    public void Actual_tag_container_queries_use_one_bounded_search()
    {
        var catalog = TagCatalogTestData.Load(TagCatalogTestData.BuildFlatCatalog(65));
        var container = catalog.CreateContainer(64);
        for (ushort i = 1; i <= 64; i++)
            container.Add(catalog.GetRequiredByIndex(i));

        for (ushort i = 1; i <= 65; i++)
        {
            _ = container.HasCore(catalog.GetRequiredByIndex(i), exact: false, out var hierarchical);
            _ = container.HasCore(catalog.GetRequiredByIndex(i), exact: true, out var exact);
            Assert.That(hierarchical, Is.LessThanOrEqualTo(7));
            Assert.That(exact, Is.LessThanOrEqualTo(7));
        }
    }

    [Test]
    public void Actual_tag_count_container_queries_use_one_bounded_search()
    {
        var catalog = TagCatalogTestData.Load(TagCatalogTestData.BuildChainCatalog(64, 16, true));
        var counts = catalog.CreateCountContainer(64);
        for (var i = 0; i < 64; i++)
            counts.Add(catalog.GetRequired(TagCatalogTestData.ChainLeaf(i, 16)));

        for (ushort i = 1; i <= catalog.Count; i++)
        {
            counts.GetCountsCore(catalog.GetRequiredByIndex(i), out _, out _, out var comparisons);
            Assert.That(comparisons, Is.LessThanOrEqualTo(11));
        }
    }

    [Test]
    public void Catalog_size_kind_depth_and_query_matrix_is_bounded_and_correct()
    {
        foreach (var catalogSize in new[] { 5_000, 50_000 })
        foreach (var exactKinds in new[] { 8, 32, 64 })
        foreach (var depth in new[] { 1, 4, 8, 16 })
        {
            var catalog = TagCatalogTestData.Load(
                TagCatalogTestData.BuildPerformanceCatalog(catalogSize, exactKinds, depth));
            var tags = catalog.CreateContainer(exactKinds);
            var counts = catalog.CreateCountContainer(exactKinds);
            var exact = new GameplayTag[exactKinds];
            var parents = new GameplayTag[exactKinds];
            var misses = new GameplayTag[exactKinds];
            for (var i = 0; i < exactKinds; i++)
            {
                exact[i] = catalog.GetRequired(TagCatalogTestData.ChainLeaf(i, depth));
                parents[i] = catalog.GetRequired("B" + i);
                misses[i] = catalog.GetRequired("F" + i);
                tags.Add(exact[i]);
                counts.Add(exact[i]);
            }

            AssertQueryCase(tags, counts, exact, expectedPerIteration: 2);
            AssertQueryCase(tags, counts, parents, expectedPerIteration: 2);
            AssertQueryCase(tags, counts, misses, expectedPerIteration: 0);
        }
    }

    private static void AssertComparisonBound(int length, int expectedMaximum)
    {
        var values = new ushort[length];
        for (var i = 0; i < length; i++)
            values[i] = checked((ushort)(i * 2 + 1));

        for (var target = 0; target <= values[length - 1] + 1; target++)
        {
            _ = TagSearch.LowerBound(values, length, checked((ushort)target), out var comparisons);
            Assert.That(comparisons, Is.LessThanOrEqualTo(expectedMaximum), $"length={length}, target={target}");
        }
    }

    private static void AssertQueryCase(
        TagContainer tags,
        TagCountContainer counts,
        GameplayTag[] queries,
        int expectedPerIteration)
    {
        for (var i = 0; i < queries.Length; i++)
        {
            _ = tags.HasCore(queries[i], exact: false, out var tagComparisons);
            counts.GetCountsCore(queries[i], out _, out _, out var countComparisons);
            Assert.That(tagComparisons, Is.LessThanOrEqualTo(7));
            Assert.That(countComparisons, Is.LessThanOrEqualTo(11));
        }

        var checksum = RunQueryBatch(tags, counts, queries, 100_000);
        Assert.That(checksum, Is.EqualTo(100_000 * expectedPerIteration));
    }

    private static int RunQueryBatch(
        TagContainer tags,
        TagCountContainer counts,
        GameplayTag[] queries,
        int iterations)
    {
        var checksum = 0;
        for (var i = 0; i < iterations; i++)
        {
            var query = queries[i % queries.Length];
            if (tags.Has(query)) checksum++;
            checksum += counts.Count(query);
        }
        return checksum;
    }
}
