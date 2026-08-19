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

    private sealed class OverflowSelector : ITargetSelector
    {
        private readonly int _desired;

        internal OverflowSelector(int desired) => _desired = desired;

        public int Select(in SelectorContext ctx, Span<TargetId> results)
        {
            var written = _desired < results.Length ? _desired : results.Length;
            for (var i = 0; i < written; i++) results[i] = new TargetId((ulong)(i + 1));
            return _desired;
        }
    }

    [Test]
    public void Selector_returning_more_than_the_buffer_increments_the_truncation_counter()
    {
        var kit = EffectTestKit.Create();
        kit.RegisterSelector("selector.everyone", new OverflowSelector(desired: 40));

        var source = EffectTestKit.MinimalInstant("source");
        var edge = EffectTestKit.Edge(ChainTrigger.OnApplication, "chained");
        edge.SelectorTag = "selector.everyone";
        source.Chains.Add(edge);
        kit.AddSpec(source);
        kit.AddSpec(EffectTestKit.MinimalInstant("chained"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("source"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(pipeline.SelectorTruncationCount, Is.EqualTo(1));
    }

    [Test]
    public void Selector_within_the_buffer_does_not_increment_the_truncation_counter()
    {
        var kit = EffectTestKit.Create();
        kit.RegisterSelector("selector.everyone", new OverflowSelector(desired: 2));

        var source = EffectTestKit.MinimalInstant("source");
        var edge = EffectTestKit.Edge(ChainTrigger.OnApplication, "chained");
        edge.SelectorTag = "selector.everyone";
        source.Chains.Add(edge);
        kit.AddSpec(source);
        kit.AddSpec(EffectTestKit.MinimalInstant("chained"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("source"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(pipeline.SelectorTruncationCount, Is.Zero);
    }

    [Test]
    public void AllTargetsSelector_with_many_targets_returns_total_count_not_written_count()
    {
        var manyTargets = new System.Collections.Generic.List<TargetId>();
        for (var i = 0; i < 40; i++) manyTargets.Add(new TargetId((ulong)(i + 1)));

        Span<TargetId> buffer = stackalloc TargetId[32];
        var selector = new EffectTestKit.AllTargetsSelector();
        selector.Bind(manyTargets);

        var selectorCtx = new SelectorContext(new TargetId(1), System.Array.Empty<BigNum>(), new XorShiftRng(1));
        var returned = selector.Select(in selectorCtx, buffer);

        Assert.That(returned, Is.EqualTo(40), "AllTargetsSelector should return total count (40), not written count (32)");
    }
}
