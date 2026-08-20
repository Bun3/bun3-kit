#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectEventChainTests
{
    private static EffectDef Damage(string name, int amount, string? eventTag)
    {
        var def = EffectTestKit.MinimalInstant(name);
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-amount) },
        });
        if (eventTag != null)
        {
            def.RaiseEvents.Add(new RaiseEventDef { Tag = eventTag, AttributeId = EffectTestKit.Hp });
        }

        return def;
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
        kit.AddEffect(Damage("bolt", 40, "event.damage.taken"));
        kit.AddEffect(Damage("retaliate", 7, eventTag: null));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge(chainEventTag, "retaliate", listen);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        kit.AddEffect(thorns);
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

        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
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

        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
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

        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)93));
        Assert.That(pipeline.EventChainFireCount, Is.EqualTo(1));
    }

    // Matching is IsAncestorOrSelf(chain's declared tag, raised event tag) -- a chain declaring the
    // parent catches descendant events (above), never the reverse. An inverted comparison would pass
    // both this test and the one above, so both directions must be checked.
    [Test]
    public void Chain_event_tag_does_not_match_ancestor_event_tags()
    {
        var kit = BuildReflectKit(ChainListen.AsTarget, chainEventTag: "event.damage.taken");
        // "bolt2" raises the ancestor tag "event.damage" -- the chain declares the descendant
        // "event.damage.taken" and must not fire for it.
        kit.AddEffect(Damage("bolt2", 40, "event.damage"));
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt2"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));
        Assert.That(pipeline.EventChainFireCount, Is.EqualTo(0));
    }

    [Test]
    public void Disabled_listener_does_not_receive_events()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("bolt", 40, "event.damage.taken"));
        kit.AddEffect(Damage("retaliate", 7, eventTag: null));

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
        kit.AddEffect(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        kit.Defender.Attributes.SetBase(EffectTestKit.Mp, 0);   // ongoing condition breaks → disabled
        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));
    }

    [Test]
    public void Event_magnitude_is_the_clamped_delta_not_the_nominal_amount()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("overkill", 500, "event.damage.taken"));
        var probe = new MagnitudeProbe();
        kit.RegisterSelector("selector.everyone", probe);

        var watcher = EffectTestKit.MinimalInfinite("watcher");
        var edge = EventEdge("event.damage.taken", "watcher", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        watcher.Chains.Add(edge);
        kit.AddEffect(watcher);
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 30);
        pipeline.EnqueueApply(kit.EffectId("watcher"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("overkill"), kit.Attacker.Id, kit.Defender.Id);
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
        kit.AddEffect(Damage("bolt", 1, "event.damage.taken"));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "bolt", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        kit.AddEffect(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline(applyBudgetPerTick: 8);

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 1000);
        }

        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();

        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        // Unbounded recursion is impossible by construction (events are dispatched through the queue,
        // never by direct re-entrant calls) — the real failure mode guarded here is a drain loop that
        // never terminates. Each processed bolt raises exactly one retaliation that re-enqueues exactly
        // one new bolt, so the queue never empties on its own; with budget 8 and one seed application,
        // draining stops after 8 applications with exactly 1 left pending.
        Assert.That(() => pipeline.Tick(), Throws.Nothing);
        Assert.That(pipeline.PendingApplyCount, Is.EqualTo(1));
    }

    [Test]
    public void Retaliation_is_attributed_to_the_listening_instances_source_not_the_event_source()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("bolt", 1, "event.damage.taken"));

        var retaliate = EffectTestKit.MinimalInstant("attributed_retaliate");
        retaliate.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.SourceAttribute(EffectTestKit.Attack, (BigNum)(-1)) },
        });
        kit.AddEffect(retaliate);

        var thorns = EffectTestKit.MinimalInfinite("thorns2");
        var edge = EventEdge("event.damage.taken", "attributed_retaliate", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        kit.AddEffect(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 1000);
        }

        kit.Attacker.Attributes.SetBase(EffectTestKit.Attack, 10);
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 50);

        pipeline.EnqueueApply(kit.EffectId("thorns2"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // Retaliation reads its caster's Attack (Defender, 50), not the attacker's (10) — proves the
        // enqueued application is attributed to the thorns owner, not the event's counterpart.
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)950));
    }

    [Test]
    public void Leech_reaction_is_attributed_to_the_vampiric_owner_when_it_has_no_selector()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("bolt", 1, "event.damage.taken"));

        var leechHeal = EffectTestKit.MinimalInstant("leech_heal");
        leechHeal.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.SourceAttribute(EffectTestKit.Attack) },
        });
        kit.AddEffect(leechHeal);

        var vampiric = EffectTestKit.MinimalInfinite("vampiric");
        vampiric.Chains.Add(EventEdge("event.damage.taken", "leech_heal", ChainListen.AsSource));
        kit.AddEffect(vampiric);
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 500);
        }

        kit.Attacker.Attributes.SetBase(EffectTestKit.Attack, 10);
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 50);

        pipeline.EnqueueApply(kit.EffectId("vampiric"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // The leech heal (no selector, so it applies to the AsSource listener itself) reads its
        // caster's Attack — the vampiric owner (Attacker, 10), not the victim (Defender, 50).
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)510));
    }

    [Test]
    public void Proportional_reflect_uses_the_clamped_event_magnitude()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("bolt", 100, "event.damage.taken"));

        var retaliate = EffectTestKit.MinimalInstant("retaliate");
        retaliate.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            // BigNum has no decimal conversion operator — (BigNum)0.3m silently truncates through the
            // decimal->long predefined conversion instead of the double one, so use FromParts.
            Magnitude = new MagnitudeDef { Base = Operand.EventMagnitude(BigNum.FromParts(3, -1)) },
        });
        kit.AddEffect(retaliate);

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "retaliate", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        kit.AddEffect(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 1000);
        }

        // Defender has less HP than the incoming damage, so the clamped delta (-50) differs from the
        // nominal one (-100) -- pins that the reflect reads the clamped Magnitude, not NominalMagnitude.
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 50);

        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)0));
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)985));
    }

    [Test]
    public void Lifesteal_flips_sign_through_a_negative_coefficient()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("bolt", 100, "event.damage.taken"));

        var leech = EffectTestKit.MinimalInstant("leech");
        leech.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.EventMagnitude(BigNum.FromParts(-1, -1)) },
        });
        kit.AddEffect(leech);

        var vampiric = EffectTestKit.MinimalInfinite("vampiric");
        vampiric.Chains.Add(EventEdge("event.damage.taken", "leech", ChainListen.AsSource));
        kit.AddEffect(vampiric);
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 500);
        }

        pipeline.EnqueueApply(kit.EffectId("vampiric"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)510));
    }

    [Test]
    public void Event_magnitude_does_not_propagate_past_the_first_hop()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("bolt", 100, "event.damage.taken"));

        var second = EffectTestKit.MinimalInstant("second");
        second.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.EventMagnitude((BigNum)1) },
        });
        kit.AddEffect(second);

        var first = EffectTestKit.MinimalInstant("first");
        first.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "second"));
        kit.AddEffect(first);

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        thorns.Chains.Add(EventEdge("event.damage.taken", "first", ChainListen.AsTarget));
        kit.AddEffect(thorns);

        // Reachability requires a direct OnEvent target — "second" is only reached transitively
        // (thorns -> first -> second) in the path this test actually exercises. This effect exists solely
        // to make "second" a legal direct OnEvent target for Build; it is registered but never applied to
        // any target, so the only path exercised below remains the two-hop one.
        var unusedWatcher = EffectTestKit.MinimalInfinite("unusedWatcher");
        unusedWatcher.Chains.Add(EventEdge("event.damage.taken", "second", ChainListen.AsTarget));
        kit.AddEffect(unusedWatcher);

        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 1000);
        }

        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.Tick();

        // bolt −100 only; "second" resolves EventMagnitude to zero because it is two hops away.
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)900));
    }

    [Test]
    public void Event_magnitude_in_an_unreachable_effect_is_a_build_error()
    {
        var kit = EffectTestKit.Create();
        var orphan = EffectTestKit.MinimalInstant("orphan");
        orphan.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.EventMagnitude((BigNum)1) },
        });
        kit.AddEffect(orphan);

        Assert.That(
            () => kit.BuildPipeline(),
            Throws.InvalidOperationException.With.Message.Contains("EventMagnitude"));
    }

    [Test]
    public void Event_magnitude_reachable_only_transitively_is_a_build_error()
    {
        // "second" is reachable only via first's OnApplication chain, never as the direct target of any
        // OnEvent edge — it would always see zero, so Build must reject it even though it is chain-reachable.
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("bolt", 100, "event.damage.taken"));

        var second = EffectTestKit.MinimalInstant("second");
        second.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.EventMagnitude((BigNum)1) },
        });
        kit.AddEffect(second);

        var first = EffectTestKit.MinimalInstant("first");
        first.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "second"));
        kit.AddEffect(first);

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        thorns.Chains.Add(EventEdge("event.damage.taken", "first", ChainListen.AsTarget));
        kit.AddEffect(thorns);

        // Tightened to the reachability rule's own phrase — "EventMagnitude" alone is also present in
        // the (unrelated) OngoingConditions rejection message and would pass for the wrong reason.
        Assert.That(
            () => kit.BuildPipeline(),
            Throws.InvalidOperationException.With.Message.Contains("no OnEvent chain applies it directly"));
    }

    [Test]
    public void Event_magnitude_in_a_clamp_bound_is_rejected()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(EffectTestKit.Hp, min: Operand.EventMagnitude((BigNum)1));

        Assert.That(() => builder.Build(), Throws.InvalidOperationException);
    }

    [Test]
    public void Event_magnitude_used_only_in_duration_scale_of_an_unreachable_effect_is_a_build_error()
    {
        // DurationScale is a MagnitudeDef whose operands are evaluated with the real magnitude via
        // ComputeScaledDuration, so the reachability scan must cover it too, not just Modifiers/Executions/
        // ApplicationConditions/ChanceToApply.
        var kit = EffectTestKit.Create();
        var scaledDuration = EffectTestKit.MinimalDuration("scaledDuration", ticks: 10);
        scaledDuration.DurationScale = new MagnitudeDef { Base = Operand.EventMagnitude() };
        kit.AddEffect(scaledDuration);

        Assert.That(
            () => kit.BuildPipeline(),
            Throws.InvalidOperationException.With.Message.Contains("EventMagnitude"));
    }

    [Test]
    public void OnEvent_edge_condition_on_event_magnitude_gates_the_reaction()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("smallHit", 30, "event.damage.taken"));
        kit.AddEffect(Damage("bigHit", 100, "event.damage.taken"));
        kit.AddEffect(Damage("retaliate", 7, eventTag: null));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "retaliate", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        // Damage magnitude is negative, so "exceeded 50" reads as "at or below -50".
        edge.Conditions.Add(new ConditionDef
        {
            Left = Operand.EventMagnitude(),
            Op = ComparisonOp.LessOrEqual,
            Right = Operand.Constant(-50),
        });
        thorns.Chains.Add(edge);
        kit.AddEffect(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 1000);
        }

        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();

        // Below threshold: -30 does not satisfy <= -50, no retaliation.
        pipeline.EnqueueApply(kit.EffectId("smallHit"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)1000));

        // Above threshold: -100 satisfies <= -50, retaliation fires.
        pipeline.EnqueueApply(kit.EffectId("bigHit"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)993));
    }

    [Test]
    public void Event_magnitude_in_a_non_event_chain_edges_conditions_is_a_build_error()
    {
        var kit = EffectTestKit.Create();
        var second = EffectTestKit.MinimalInstant("second");
        kit.AddEffect(second);

        var first = EffectTestKit.MinimalInstant("first");
        var edge = EffectTestKit.Edge(ChainTrigger.OnApplication, "second");
        edge.Conditions.Add(new ConditionDef
        {
            Left = Operand.EventMagnitude(),
            Op = ComparisonOp.Greater,
            Right = Operand.Constant(0),
        });
        first.Chains.Add(edge);
        kit.AddEffect(first);

        Assert.That(
            () => kit.BuildPipeline(),
            Throws.InvalidOperationException.With.Message.Contains("EventMagnitude"));
    }

    [Test]
    public void Event_magnitude_in_a_periodic_effects_modifiers_is_a_build_error()
    {
        var kit = EffectTestKit.Create();
        var periodic = EffectTestKit.MinimalDuration("periodicHeal", ticks: 30);
        periodic.PeriodTicks = 5;
        periodic.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.EventMagnitude() },
        });
        kit.AddEffect(periodic);

        Assert.That(
            () => kit.BuildPipeline(),
            Throws.InvalidOperationException.With.Message.Contains("periodic"));
    }

    [Test]
    public void Periodic_effect_using_event_magnitude_only_in_application_conditions_still_builds()
    {
        var kit = EffectTestKit.Create();
        var periodic = EffectTestKit.MinimalDuration("periodicGuarded", ticks: 30);
        periodic.PeriodTicks = 5;
        periodic.ApplicationConditions.Add(new ConditionDef
        {
            Left = Operand.EventMagnitude(),
            Op = ComparisonOp.LessOrEqual,
            Right = Operand.Constant(0),
        });
        kit.AddEffect(periodic);

        // Makes "periodicGuarded" a legal direct OnEvent target so reachability passes; never applied.
        var watcher = EffectTestKit.MinimalInfinite("watcher");
        watcher.Chains.Add(EventEdge("event.damage.taken", "periodicGuarded", ChainListen.AsTarget));
        kit.AddEffect(watcher);

        Assert.That(() => kit.BuildPipeline(), Throws.Nothing);
    }

    [Test]
    public void Built_in_event_source_selector_targets_the_attacker()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("bolt", 40, "event.damage.taken"));
        kit.AddEffect(Damage("retaliate", 7, eventTag: null));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "retaliate", ChainListen.AsTarget);
        edge.SelectorTag = "selector.event.source";
        thorns.Chains.Add(edge);
        kit.AddEffect(thorns);
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.EffectId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)93));
    }

    [Test]
    public void Built_in_event_target_selector_targets_the_victim()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("bolt", 40, "event.damage.taken"));
        kit.AddEffect(Damage("followup", 5, eventTag: null));

        var vampiric = EffectTestKit.MinimalInfinite("vampiric");
        var edge = EventEdge("event.damage.taken", "followup", ChainListen.AsSource);
        edge.SelectorTag = "selector.event.target";
        vampiric.Chains.Add(edge);
        kit.AddEffect(vampiric);
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.EffectId("vampiric"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)55));
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));
    }

    [Test]
    public void Event_selector_on_a_non_event_chain_edge_is_a_build_error()
    {
        var kit = EffectTestKit.Create();
        var second = EffectTestKit.MinimalInstant("second");
        kit.AddEffect(second);

        var first = EffectTestKit.MinimalInstant("first");
        var edge = EffectTestKit.Edge(ChainTrigger.OnApplication, "second");
        edge.SelectorTag = BuiltInSelectors.EventSourceTag;
        first.Chains.Add(edge);
        kit.AddEffect(first);

        Assert.That(
            () => kit.BuildPipeline(),
            Throws.InvalidOperationException.With.Message.Contains("only meaningful on an OnEvent"));
    }

    [Test]
    public void Event_selector_on_an_OnEvent_chain_edge_still_builds()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(Damage("bolt", 1, "event.damage.taken"));
        kit.AddEffect(Damage("retaliate", 1, eventTag: null));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "retaliate", ChainListen.AsTarget);
        edge.SelectorTag = BuiltInSelectors.EventSourceTag;
        thorns.Chains.Add(edge);
        kit.AddEffect(thorns);

        Assert.That(() => kit.BuildPipeline(), Throws.Nothing);
    }
}
