#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TargetSelectorTests
{
    private sealed class RecordingSelector : ITargetSelector
    {
        internal readonly List<TargetId> SeenCandidates = new List<TargetId>();
        internal TargetId SeenOrigin;
        internal BigNum SeenHp;
        internal bool SawTags;

        public int Select(in SelectorContext ctx, Span<TargetId> results)
        {
            SeenOrigin = ctx.Origin;
            for (var i = 0; i < ctx.CandidateCount; i++) SeenCandidates.Add(ctx.CandidateAt(i));
            ctx.TryGetAttribute(ctx.Origin, EffectTestKit.Hp, out SeenHp);
            SawTags = ctx.TryGetTags(ctx.Origin, out _);
            return 0;
        }
    }

    [Test]
    public void Selector_sees_all_candidates_in_ascending_order_plus_origin_and_attributes()
    {
        var kit = EffectTestKit.Create();
        var selector = new RecordingSelector();
        kit.RegisterSelector("selector.everyone", selector);

        var source = EffectTestKit.MinimalInstant("source");
        var edge = EffectTestKit.Edge(ChainTrigger.OnApplication, "chained");
        edge.SelectorTag = "selector.everyone";
        source.Chains.Add(edge);
        kit.AddEffect(source);
        kit.AddEffect(EffectTestKit.MinimalInstant("chained"));
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 55);
        pipeline.EnqueueApply(kit.EffectId("source"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(selector.SeenOrigin, Is.EqualTo(kit.Defender.Id));
        Assert.That(selector.SeenCandidates.Count, Is.EqualTo(kit.AllTargets.Count));
        for (var i = 1; i < selector.SeenCandidates.Count; i++)
        {
            Assert.That(selector.SeenCandidates[i], Is.GreaterThan(selector.SeenCandidates[i - 1]));
        }

        Assert.That(selector.SeenHp, Is.EqualTo((BigNum)55));
        Assert.That(selector.SawTags, Is.True);
    }

    [Test]
    public void Unknown_candidate_lookups_report_failure_without_throwing()
    {
        var kit = EffectTestKit.Create();
        var probe = new UnknownProbeSelector();
        kit.RegisterSelector("selector.everyone", probe);

        var source = EffectTestKit.MinimalInstant("source");
        var edge = EffectTestKit.Edge(ChainTrigger.OnApplication, "chained");
        edge.SelectorTag = "selector.everyone";
        source.Chains.Add(edge);
        kit.AddEffect(source);
        kit.AddEffect(EffectTestKit.MinimalInstant("chained"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("source"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(probe.TagsFound, Is.False);
        Assert.That(probe.AttributeFound, Is.False);
        Assert.That(probe.AttributeValue, Is.EqualTo(BigNum.Zero));
    }

    private sealed class UnknownProbeSelector : ITargetSelector
    {
        internal bool TagsFound;
        internal bool AttributeFound;
        internal BigNum AttributeValue;

        public int Select(in SelectorContext ctx, Span<TargetId> results)
        {
            var missing = new TargetId(ulong.MaxValue);
            TagsFound = ctx.TryGetTags(missing, out _);
            AttributeFound = ctx.TryGetAttribute(missing, EffectTestKit.Hp, out AttributeValue);
            return 0;
        }
    }
}
