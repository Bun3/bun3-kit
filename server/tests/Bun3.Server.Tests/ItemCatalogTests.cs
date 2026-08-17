using Bun3.Server.Items;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class ItemCatalogTests
{
    private sealed record TestDef(string DisplayName);

    private static ItemCatalog<TestDef> BuildSample() =>
        new ItemCatalogBuilder<TestDef>()
            .Register("potion.small", new TestDef("소형 물약"), maxStack: 999)
            .Register("gold", new TestDef("골드"))
            .Build();

    [Test]
    public void Build_interns_id_strings_and_resolves_ids()
    {
        var catalog = BuildSample();

        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.TryGet("potion.small", out var potion), Is.True);
        Assert.That(catalog.Contains(potion), Is.True);
        Assert.That(catalog.GetIdString(potion), Is.EqualTo("potion.small"));
        // 인터닝 — 같은 참조를 돌려준다
        Assert.That(ReferenceEquals(catalog.GetIdString(potion), catalog.GetIdString(potion)), Is.True);
        Assert.That(catalog.GetDefinition(potion).DisplayName, Is.EqualTo("소형 물약"));
        Assert.That(catalog.GetMaxStack(potion), Is.EqualTo(999));
        Assert.That(catalog.GetMaxStack(catalog.GetRequired("gold")), Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void Lookup_misses_and_invalid_ids_are_rejected()
    {
        var catalog = BuildSample();

        Assert.That(catalog.TryGet("nope", out var missing), Is.False);
        Assert.That(missing, Is.EqualTo(ItemId.None));
        Assert.That(() => catalog.GetRequired("nope"), Throws.TypeOf<ItemCatalogException>());
        Assert.That(catalog.Contains(ItemId.None), Is.False);
        Assert.That(() => catalog.GetIdString(ItemId.None), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => catalog.GetDefinition(ItemId.None), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Register_rejects_duplicate_empty_id_and_bad_max_stack()
    {
        var builder = new ItemCatalogBuilder<TestDef>().Register("gold", new TestDef("골드"));

        Assert.That(() => builder.Register("gold", new TestDef("중복")), Throws.TypeOf<ItemCatalogException>());
        Assert.That(() => builder.Register("  ", new TestDef("공백")), Throws.ArgumentException);
        Assert.That(() => builder.Register("x", new TestDef("x"), maxStack: 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Build_runs_validators_and_propagates_failure()
    {
        var ran = 0;
        var builder = new ItemCatalogBuilder<TestDef>()
            .Register("gold", new TestDef("골드"))
            .AddValidator(_ => ran++)
            .AddValidator(c => throw new ItemCatalogException($"검증 실패: {c.Count}개"));

        Assert.That(() => builder.Build(), Throws.TypeOf<ItemCatalogException>());
        Assert.That(ran, Is.EqualTo(1));
    }

    [Test]
    public void External_id_maps_both_directions_and_is_optional()
    {
        var catalog = new ItemCatalogBuilder<TestDef>()
            .Register("potion.small", new TestDef("소형 물약"), externalId: 1021)
            .Register("gold", new TestDef("골드"))
            .Build();
        var potion = catalog.GetRequired("potion.small");
        var gold = catalog.GetRequired("gold");

        Assert.That(catalog.TryGetExternalId(potion, out var externalId), Is.True);
        Assert.That(externalId, Is.EqualTo(1021));
        Assert.That(catalog.TryGetByExternalId(1021, out var resolved), Is.True);
        Assert.That(resolved, Is.EqualTo(potion));

        Assert.That(catalog.TryGetExternalId(gold, out _), Is.False);
        Assert.That(catalog.TryGetByExternalId(9999, out var missing), Is.False);
        Assert.That(missing, Is.EqualTo(ItemId.None));
        Assert.That(() => catalog.TryGetExternalId(ItemId.None, out _),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Register_rejects_duplicate_and_reserved_external_id()
    {
        var builder = new ItemCatalogBuilder<TestDef>()
            .Register("a", new TestDef("a"), externalId: 7);

        Assert.That(() => builder.Register("b", new TestDef("b"), externalId: 7),
            Throws.TypeOf<ItemCatalogException>());
        Assert.That(() => builder.Register("c", new TestDef("c"), externalId: long.MinValue),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Indexes_are_declared_before_build_and_queried_after()
    {
        var builder = new ItemCatalogBuilder<TestDef>();
        var byName = builder.CreateIndex(def => def.DisplayName);
        var byChar = builder.CreateMultiIndex(def => def.DisplayName.ToCharArray().Distinct());

        Assert.That(() => byName.Get("골드"), Throws.InvalidOperationException, "Build 전 조회 금지");

        builder.Register("gold", new TestDef("골드"))
            .Register("gold2", new TestDef("골드"))
            .Register("gem", new TestDef("보석"))
            .Build();

        Assert.That(byName.Get("골드").Length, Is.EqualTo(2));
        Assert.That(byName.Get("보석").Length, Is.EqualTo(1));
        Assert.That(byName.Get("없음").Length, Is.EqualTo(0), "미등록 키는 빈 span");
        Assert.That(byChar.Get('골').Length, Is.EqualTo(2));
        Assert.That(byChar.Contains('석'), Is.True);
    }

    [Test]
    public void Build_is_single_use()
    {
        var builder = new ItemCatalogBuilder<TestDef>().Register("gold", new TestDef("골드"));
        builder.Build();

        Assert.That(() => builder.Build(), Throws.InvalidOperationException);
        Assert.That(() => builder.Register("x", new TestDef("x")), Throws.InvalidOperationException);
    }
}
