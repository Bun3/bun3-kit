using System;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

/// <summary>
/// 프로세스 내 카탈로그가 하나라는 계약을 컨테이너가 강제하는지 검사한다. 다른 카탈로그의
/// 태그는 index만으로 전부 걸러낼 수 없지만, 최소한 범위를 벗어난 index는 조용히 통과하면 안 된다.
/// </summary>
[TestFixture]
public sealed class TagSingleCatalogContractTests
{
    private TagCatalog _catalog = null!;
    private GameplayTag _foreign;

    [SetUp]
    public void SetUp()
    {
        _catalog = TagCatalogTestData.Load();
        _foreign = new GameplayTag(checked((ushort)(_catalog.Count + 1)));
    }

    [Test]
    public void Container_mutation_rejects_a_tag_outside_the_catalog()
    {
        var tags = _catalog.CreateContainer();
        Assert.Throws<ArgumentOutOfRangeException>(() => tags.Add(_foreign));
        Assert.Throws<ArgumentOutOfRangeException>(() => tags.Remove(_foreign));
    }

    [Test]
    public void Count_container_mutation_rejects_a_tag_outside_the_catalog()
    {
        var counts = _catalog.CreateCountContainer();
        Assert.Throws<ArgumentOutOfRangeException>(() => counts.Add(_foreign));
        Assert.Throws<ArgumentOutOfRangeException>(() => counts.Remove(_foreign));
    }

    [Test]
    public void Queries_stay_allocation_free_and_treat_an_outside_tag_as_a_miss()
    {
        // 조회는 틱 핫패스이므로 예외 대신 미일치로 처리한다.
        var tags = _catalog.CreateContainer();
        tags.Add(_catalog.GetRequired("State.Dead.Ghost"));

        Assert.That(tags.Has(_foreign), Is.False);
        Assert.That(tags.HasExact(_foreign), Is.False);

        var counts = _catalog.CreateCountContainer();
        counts.Add(_catalog.GetRequired("State.Dead.Ghost"));
        Assert.That(counts.Has(_foreign), Is.False);
        Assert.That(counts.HasExact(_foreign), Is.False);
    }

    [Test]
    public void Cross_catalog_queries_are_rejected_by_reference_identity()
    {
        var tags = _catalog.CreateContainer();
        var otherCatalogQuery = TagCatalogTestData.Load().CreateContainer();

        Assert.Throws<ArgumentException>(() => tags.HasAny(otherCatalogQuery));
        Assert.Throws<ArgumentException>(() => tags.HasAll(otherCatalogQuery));
        Assert.Throws<ArgumentException>(() => tags.HasAnyExact(otherCatalogQuery));
        Assert.Throws<ArgumentException>(() => tags.HasAllExact(otherCatalogQuery));
    }
}
