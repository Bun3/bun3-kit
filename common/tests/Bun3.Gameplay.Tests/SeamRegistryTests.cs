#nullable enable
using System;
using System.IO;
using System.Text;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class SeamRegistryTests
{
    private static TagCatalog LoadCatalog()
    {
        const string json = "{\"schemaVersion\":1,\"tags\":[" +
            "{\"name\":\"calc.magnitude.x\"},{\"name\":\"calc.execution.dmg\"}," +
            "{\"name\":\"selector.team\"},{\"name\":\"state.dead\"}]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return TagCatalogJson.Load(stream);
    }

    private sealed class FixedMagnitude : IMagnitudeCalc
    {
        public BigNum Calculate(in MagnitudeContext ctx) => 7;
    }

    [Test]
    public void Registered_seam_resolves_by_tag()
    {
        var catalog = LoadCatalog();
        var builder = new SeamRegistryBuilder();
        var calc = new FixedMagnitude();
        builder.RegisterMagnitudeCalc(catalog.GetRequired("calc.magnitude.x"), calc);
        var registry = builder.Build(catalog);
        Assert.That(registry.GetMagnitudeCalc(catalog.GetRequired("calc.magnitude.x")), Is.SameAs(calc));
    }

    [Test]
    public void Build_rejects_wrong_root_duplicate_and_root_itself()
    {
        var catalog = LoadCatalog();

        var wrongRoot = new SeamRegistryBuilder();
        wrongRoot.RegisterMagnitudeCalc(catalog.GetRequired("state.dead"), new FixedMagnitude());
        Assert.Throws<InvalidOperationException>(() => wrongRoot.Build(catalog));

        var duplicated = new SeamRegistryBuilder();
        duplicated.RegisterMagnitudeCalc(catalog.GetRequired("calc.magnitude.x"), new FixedMagnitude());
        Assert.Throws<InvalidOperationException>(
            () => duplicated.RegisterMagnitudeCalc(catalog.GetRequired("calc.magnitude.x"), new FixedMagnitude()));

        var rootItself = new SeamRegistryBuilder();
        rootItself.RegisterMagnitudeCalc(catalog.GetRequired("calc.magnitude"), new FixedMagnitude());
        Assert.Throws<InvalidOperationException>(() => rootItself.Build(catalog));
    }

    [Test]
    public void Build_then_register_throws()
    {
        var catalog = LoadCatalog();
        var builder = new SeamRegistryBuilder();
        builder.Build(catalog);
        Assert.Throws<InvalidOperationException>(
            () => builder.RegisterMagnitudeCalc(catalog.GetRequired("calc.magnitude.x"), new FixedMagnitude()));
    }

    [Test]
    public void XorShift_is_deterministic_and_rejects_zero_seed()
    {
        var a = new XorShiftRng(42);
        var b = new XorShiftRng(42);
        for (var i = 0; i < 100; i++)
            Assert.That(a.NextUInt32(), Is.EqualTo(b.NextUInt32()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new XorShiftRng(0));
    }
}
