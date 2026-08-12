using System;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagContainerTests
{
    private TagCatalog _catalog = null!;
    private GameplayTag _state;
    private GameplayTag _dead;
    private GameplayTag _ghost;
    private GameplayTag _rooted;

    [SetUp]
    public void SetUp()
    {
        _catalog = TagCatalogTestData.Load();
        _state = _catalog.GetRequired("State");
        _dead = _catalog.GetRequired("State.Dead");
        _ghost = _catalog.GetRequired("State.Dead.Ghost");
        _rooted = _catalog.GetRequired("State.Rooted");
    }

    [Test]
    public void Add_is_unique_and_remove_affects_only_explicit_tag()
    {
        var tags = _catalog.CreateContainer();
        Assert.That(tags.Add(_ghost), Is.True);
        Assert.That(tags.Add(_ghost), Is.False);
        Assert.That(tags.ExactKindCount, Is.EqualTo(1));
        Assert.That(tags.HasExact(_ghost), Is.True);
        Assert.That(tags.HasExact(_dead), Is.False);
        Assert.That(tags.Remove(_dead), Is.False);
        Assert.That(tags.Remove(_ghost), Is.True);
    }

    [Test]
    public void Hierarchy_matches_from_owned_child_to_queried_parent_only()
    {
        var tags = _catalog.CreateContainer();
        tags.Add(_ghost);

        Assert.That(tags.Has(_state), Is.True);
        Assert.That(tags.Has(_dead), Is.True);
        Assert.That(tags.Has(_ghost), Is.True);
        Assert.That(tags.Has(_rooted), Is.False);

        var parentOnly = _catalog.CreateContainer();
        parentOnly.Add(_state);
        Assert.That(parentOnly.Has(_ghost), Is.False);
    }

    [Test]
    public void Any_all_and_exact_variants_have_explicit_empty_query_semantics()
    {
        var owned = _catalog.CreateContainer();
        owned.Add(_ghost);
        owned.Add(_rooted);

        var query = _catalog.CreateContainer();
        query.Add(_dead);
        query.Add(_rooted);
        Assert.That(owned.HasAny(query), Is.True);
        Assert.That(owned.HasAll(query), Is.True);
        Assert.That(owned.HasAnyExact(query), Is.True);
        Assert.That(owned.HasAllExact(query), Is.False);

        var empty = _catalog.CreateContainer();
        Assert.That(owned.HasAny(empty), Is.False);
        Assert.That(owned.HasAll(empty), Is.True);
        Assert.That(owned.HasAnyExact(empty), Is.False);
        Assert.That(owned.HasAllExact(empty), Is.True);
    }

    [Test]
    public void None_cross_catalog_and_capacity_fail_atomically()
    {
        Assert.DoesNotThrow(() => _catalog.CreateContainer(0));
        Assert.DoesNotThrow(() => _catalog.CreateContainer(64));
        Assert.Throws<ArgumentOutOfRangeException>(() => _catalog.CreateContainer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _catalog.CreateContainer(65));

        var owned = _catalog.CreateContainer(64);
        Assert.That(owned.Has(GameplayTag.None), Is.False);
        Assert.That(owned.HasExact(GameplayTag.None), Is.False);
        Assert.Throws<ArgumentException>(() => owned.Add(GameplayTag.None));
        Assert.Throws<ArgumentException>(() => owned.Remove(GameplayTag.None));

        var other = TagCatalogTestData.Load().CreateContainer();
        Assert.Throws<ArgumentException>(() => owned.HasAny(other));

        var flat = TagCatalogTestData.Load(TagCatalogTestData.BuildFlatCatalog(65));
        var full = flat.CreateContainer(64);
        for (ushort i = 1; i <= 64; i++)
            Assert.That(full.Add(flat.GetRequiredByIndex(i)), Is.True);
        Assert.Throws<InvalidOperationException>(() => full.Add(flat.GetRequiredByIndex(65)));
        Assert.That(full.ExactKindCount, Is.EqualTo(64));
        Assert.That(full.HasExact(flat.GetRequiredByIndex(65)), Is.False);
    }
}
