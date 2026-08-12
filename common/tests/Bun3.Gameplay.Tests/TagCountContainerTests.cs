using System;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagCountContainerTests
{
    private TagCatalog _catalog = null!;
    private TagCountContainer _counts = null!;
    private GameplayTag _state;
    private GameplayTag _dead;
    private GameplayTag _ghost;
    private GameplayTag _rooted;

    [SetUp]
    public void SetUp()
    {
        _catalog = TagCatalogTestData.Load();
        _counts = _catalog.CreateCountContainer(8);
        _state = _catalog.GetRequired("State");
        _dead = _catalog.GetRequired("State.Dead");
        _ghost = _catalog.GetRequired("State.Dead.Ghost");
        _rooted = _catalog.GetRequired("State.Rooted");
    }

    [Test]
    public void Multiple_sources_update_exact_and_all_ancestors()
    {
        _counts.Add(_ghost, 2);
        _counts.Add(_dead, 1);

        Assert.That(_counts.ExactKindCount, Is.EqualTo(2));
        Assert.That(_counts.ExactCount(_ghost), Is.EqualTo(2));
        Assert.That(_counts.ExactCount(_dead), Is.EqualTo(1));
        Assert.That(_counts.Count(_ghost), Is.EqualTo(2));
        Assert.That(_counts.Count(_dead), Is.EqualTo(3));
        Assert.That(_counts.Count(_state), Is.EqualTo(3));
        Assert.That(_counts.Has(_state), Is.True);
        Assert.That(_counts.HasExact(_state), Is.False);
    }

    [Test]
    public void Siblings_contribute_to_common_parent()
    {
        _counts.Add(_ghost, 2);
        _counts.Add(_rooted, 4);

        Assert.That(_counts.Count(_state), Is.EqualTo(6));
        Assert.That(_counts.Count(_dead), Is.EqualTo(2));
    }

    [Test]
    public void Remove_returns_actual_amount_and_clamps_at_zero()
    {
        _counts.Add(_ghost, 3);
        Assert.That(_counts.Remove(_ghost, 2), Is.EqualTo(2));
        Assert.That(_counts.ExactCount(_ghost), Is.EqualTo(1));
        Assert.That(_counts.Remove(_ghost, 99), Is.EqualTo(1));
        Assert.That(_counts.Remove(_ghost), Is.Zero);
        Assert.That(_counts.Count(_state), Is.Zero);
        Assert.That(_counts.ExactKindCount, Is.Zero);
    }

    [Test]
    public void Removing_an_earlier_exact_kind_keeps_later_kind_and_decrements_kind_count()
    {
        _counts.Add(_dead);
        _counts.Add(_rooted);

        Assert.That(_counts.Remove(_dead), Is.EqualTo(1));
        Assert.That(_counts.ExactKindCount, Is.EqualTo(1));
        Assert.That(_counts.HasExact(_dead), Is.False);
        Assert.That(_counts.HasExact(_rooted), Is.True);
    }

    [Test]
    public void None_nonpositive_and_capacity_fail_without_mutation()
    {
        Assert.DoesNotThrow(() => _catalog.CreateCountContainer(0));
        Assert.DoesNotThrow(() => _catalog.CreateCountContainer(64));
        Assert.Throws<ArgumentOutOfRangeException>(() => _catalog.CreateCountContainer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _catalog.CreateCountContainer(65));

        Assert.Throws<ArgumentException>(() => _counts.Add(GameplayTag.None));
        Assert.Throws<ArgumentException>(() => _counts.Remove(GameplayTag.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => _counts.Add(_ghost, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _counts.Add(_ghost, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _counts.Remove(_ghost, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _counts.Remove(_ghost, -1));
        Assert.That(_counts.ExactKindCount, Is.Zero);
        Assert.That(_counts.Has(GameplayTag.None), Is.False);
        Assert.That(_counts.HasExact(GameplayTag.None), Is.False);
    }

    [Test]
    public void Any_all_exact_and_empty_query_match_tag_container_semantics()
    {
        _counts.Add(_ghost);
        _counts.Add(_rooted);
        var query = _catalog.CreateContainer();
        query.Add(_dead);
        query.Add(_rooted);

        Assert.That(_counts.HasAny(query), Is.True);
        Assert.That(_counts.HasAll(query), Is.True);
        Assert.That(_counts.HasAnyExact(query), Is.True);
        Assert.That(_counts.HasAllExact(query), Is.False);

        var empty = _catalog.CreateContainer();
        Assert.That(_counts.HasAny(empty), Is.False);
        Assert.That(_counts.HasAll(empty), Is.True);
        Assert.That(_counts.HasAnyExact(empty), Is.False);
        Assert.That(_counts.HasAllExact(empty), Is.True);
        Assert.Throws<ArgumentException>(() => _counts.HasAny(TagCatalogTestData.Load().CreateContainer()));
    }

    [Test]
    public void Exact_overflow_keeps_every_entry_unchanged()
    {
        _counts.Add(_ghost, int.MaxValue);
        Assert.Throws<OverflowException>(() => _counts.Add(_ghost));
        Assert.That(_counts.ExactCount(_ghost), Is.EqualTo(int.MaxValue));
        Assert.That(_counts.Count(_state), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void Aggregate_overflow_keeps_sibling_and_parent_counts_unchanged()
    {
        _counts.Add(_ghost, int.MaxValue);
        Assert.Throws<OverflowException>(() => _counts.Add(_rooted));
        Assert.That(_counts.ExactCount(_rooted), Is.Zero);
        Assert.That(_counts.ExactCount(_ghost), Is.EqualTo(int.MaxValue));
        Assert.That(_counts.Count(_state), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void Sixty_fifth_exact_kind_fails_atomically()
    {
        var flat = TagCatalogTestData.Load(TagCatalogTestData.BuildFlatCatalog(65));
        var counts = flat.CreateCountContainer(64);
        for (ushort i = 1; i <= 64; i++)
            counts.Add(flat.GetRequiredByIndex(i));

        Assert.Throws<InvalidOperationException>(() => counts.Add(flat.GetRequiredByIndex(65)));
        Assert.That(counts.ExactKindCount, Is.EqualTo(64));
        Assert.That(counts.ExactCount(flat.GetRequiredByIndex(65)), Is.Zero);
        Assert.That(counts.Count(flat.GetRequiredByIndex(65)), Is.Zero);
    }

    [Test]
    public void Mutation_uses_at_most_one_merge_or_compact_pass()
    {
        _counts.Add(_ghost);
        Assert.That(_counts.LastMutationPassCount, Is.EqualTo(1));
        Assert.That(_counts.LastMutationDepth, Is.LessThanOrEqualTo(16));
        _counts.Add(_rooted);
        Assert.That(_counts.LastMutationPassCount, Is.EqualTo(1));
        Assert.That(_counts.LastMutationDepth, Is.LessThanOrEqualTo(16));
        _counts.Remove(_ghost);
        Assert.That(_counts.LastMutationPassCount, Is.EqualTo(1));
        Assert.That(_counts.LastMutationDepth, Is.LessThanOrEqualTo(16));
    }

    [Test]
    public void Actual_count_queries_use_one_bounded_search()
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
    public void Maximum_exact_kind_and_depth_combination_tracks_all_aggregate_entries()
    {
        var catalog = TagCatalogTestData.Load(TagCatalogTestData.BuildChainCatalog(64, 16, false));
        var counts = catalog.CreateCountContainer(64);
        for (var i = 0; i < 64; i++)
            counts.Add(catalog.GetRequired(TagCatalogTestData.ChainLeaf(i, 16)));

        Assert.That(counts.ExactKindCount, Is.EqualTo(64));
        Assert.That(counts.Count(catalog.GetRequired(TagCatalogTestData.ChainLeaf(63, 16))), Is.EqualTo(1));
    }
}
