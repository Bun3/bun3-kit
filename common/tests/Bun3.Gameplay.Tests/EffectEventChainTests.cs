#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectEventChainTests
{
    private static EffectSpec Damage(string name, int amount, string? eventTag)
    {
        var spec = EffectTestKit.MinimalInstant(name);
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-amount) },
        });
        if (eventTag != null)
        {
            spec.RaiseEvents.Add(new RaiseEventDef { Tag = eventTag, AttributeId = EffectTestKit.Hp });
        }

        return spec;
    }

    private static ChainEdgeDef EventEdge(string eventTag, string effectName, ChainListen listen)
    {
        var edge = EffectTestKit.Edge(ChainTrigger.OnEvent, effectName);
        edge.EventTag = eventTag;
        edge.Listen = listen;
        return edge;
    }

    private static EffectTestKit BuildReflectKit(ChainListen listen, string chainEventTag = "event.damage.taken")
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 40, "event.damage.taken"));
        kit.AddSpec(Damage("retaliate", 7, eventTag: null));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge(chainEventTag, "retaliate", listen);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        kit.AddSpec(thorns);
        return kit;
    }

    private sealed class AttackerSelector : Bun3.Gameplay.Seams.ITargetSelector
    {
        public int Select(in Bun3.Gameplay.Seams.SelectorContext ctx, System.Span<TargetId> results)
        {
            results[0] = ctx.EventSource;
            return 1;
        }
    }

    [Test]
    public void AsTarget_listener_retaliates_against_the_event_source()
    {
        var kit = BuildReflectKit(ChainListen.AsTarget);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)60));
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)93));
    }

    [Test]
    public void AsSource_listener_does_not_fire_for_a_target_side_event()
    {
        var kit = BuildReflectKit(ChainListen.AsSource);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));
    }

    [Test]
    public void Chain_event_tag_matches_descendant_event_tags()
    {
        var kit = BuildReflectKit(ChainListen.AsTarget, chainEventTag: "event.damage");
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)93));
    }

    [Test]
    public void Disabled_listener_does_not_receive_events()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 40, "event.damage.taken"));
        kit.AddSpec(Damage("retaliate", 7, eventTag: null));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "retaliate", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        thorns.OngoingConditions.Add(new ConditionDef
        {
            Left = Operand.Attribute(EffectTestKit.Mp),
            Op = ComparisonOp.Greater,
            Right = Operand.Constant(0),
        });
        kit.AddSpec(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        kit.Defender.Attributes.SetBase(EffectTestKit.Mp, 0);   // ongoing condition breaks → disabled
        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));
    }

    [Test]
    public void Event_magnitude_is_the_clamped_delta_not_the_nominal_amount()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("overkill", 500, "event.damage.taken"));
        var probe = new MagnitudeProbe();
        kit.RegisterSelector("selector.everyone", probe);

        var watcher = EffectTestKit.MinimalInfinite("watcher");
        var edge = EventEdge("event.damage.taken", "watcher", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        watcher.Chains.Add(edge);
        kit.AddSpec(watcher);
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 30);
        pipeline.EnqueueApply(kit.SpecId("watcher"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("overkill"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(probe.LastMagnitude, Is.EqualTo((BigNum)(-30)));
        Assert.That(probe.LastNominal, Is.EqualTo((BigNum)(-500)));
    }

    private sealed class MagnitudeProbe : Bun3.Gameplay.Seams.ITargetSelector
    {
        internal BigNum LastMagnitude;
        internal BigNum LastNominal;

        public int Select(in Bun3.Gameplay.Seams.SelectorContext ctx, System.Span<TargetId> results)
        {
            LastMagnitude = ctx.EventMagnitude;
            LastNominal = ctx.EventNominalMagnitude;
            return 0;
        }
    }

    [Test]
    public void Mutual_reflection_is_bounded_by_the_apply_budget()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 1, "event.damage.taken"));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "bolt", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        kit.AddSpec(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline(applyBudgetPerTick: 8);

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 1000);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();

        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        Assert.That(() => pipeline.Tick(), Throws.Nothing);   // budget must stop the loop, not a stack overflow
        Assert.That(pipeline.PendingApplyCount, Is.GreaterThan(0));
    }

    [Test]
    public void Retaliation_is_attributed_to_the_listening_instances_source_not_the_event_source()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 1, "event.damage.taken"));

        var retaliate = EffectTestKit.MinimalInstant("attributed_retaliate");
        retaliate.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.SourceAttribute(EffectTestKit.Attack, (BigNum)(-1)) },
        });
        kit.AddSpec(retaliate);

        var thorns = EffectTestKit.MinimalInfinite("thorns2");
        var edge = EventEdge("event.damage.taken", "attributed_retaliate", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        kit.AddSpec(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 1000);
        }

        kit.Attacker.Attributes.SetBase(EffectTestKit.Attack, 10);
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 50);

        pipeline.EnqueueApply(kit.SpecId("thorns2"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // Retaliation reads its caster's Attack (Defender, 50), not the attacker's (10) — proves the
        // enqueued application is attributed to the thorns owner, not the event's counterpart.
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)950));
    }

    [Test]
    public void Leech_reaction_is_attributed_to_the_vampiric_owner_when_it_has_no_selector()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 1, "event.damage.taken"));

        var leechHeal = EffectTestKit.MinimalInstant("leech_heal");
        leechHeal.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.SourceAttribute(EffectTestKit.Attack) },
        });
        kit.AddSpec(leechHeal);

        var vampiric = EffectTestKit.MinimalInfinite("vampiric");
        vampiric.Chains.Add(EventEdge("event.damage.taken", "leech_heal", ChainListen.AsSource));
        kit.AddSpec(vampiric);
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 500);
        }

        kit.Attacker.Attributes.SetBase(EffectTestKit.Attack, 10);
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 50);

        pipeline.EnqueueApply(kit.SpecId("vampiric"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // The leech heal (no selector, so it applies to the AsSource listener itself) reads its
        // caster's Attack — the vampiric owner (Attacker, 10), not the victim (Defender, 50).
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)510));
    }
}
