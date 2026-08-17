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
    public void Build_is_single_use()
    {
        var builder = new ItemCatalogBuilder<TestDef>().Register("gold", new TestDef("골드"));
        builder.Build();

        Assert.That(() => builder.Build(), Throws.InvalidOperationException);
        Assert.That(() => builder.Register("x", new TestDef("x")), Throws.InvalidOperationException);
    }
}
