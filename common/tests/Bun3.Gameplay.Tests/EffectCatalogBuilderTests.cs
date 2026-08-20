using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectCatalogBuilderTests
{
    private sealed class FixedMagnitude : IMagnitudeCalc
    {
        public BigNum Calculate(in MagnitudeContext ctx) => 7;
    }

    // Rule 1: names must be non-empty and unique.

    [Test]
    public void Build_rejects_empty_name()
    {
        var builder = new EffectCatalogBuilder();
        builder.Add(EffectTestKit.MinimalInstant(""));
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void Build_rejects_duplicate_name()
    {
        var builder = new EffectCatalogBuilder();
        builder.Add(EffectTestKit.MinimalInstant("dup"));
        builder.Add(EffectTestKit.MinimalInstant("dup"));
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("dup"));
    }

    // Rule 2: Instant effects must leave Duration-only fields empty.

    [Test]
    public void Build_rejects_instant_with_duration_only_fields()
    {
        var def = EffectTestKit.MinimalInstant("bad");
        def.GrantedTags.Add("state.dead");
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("bad"));
    }

    // Rule 3: DurationTicks constraints for Duration/Infinite.

    [Test]
    public void Build_rejects_duration_type_with_nonpositive_ticks()
    {
        var builder = new EffectCatalogBuilder();
        builder.Add(EffectTestKit.MinimalDuration("d", 0));
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("d"));
    }

    [Test]
    public void Build_rejects_infinite_type_with_nonzero_ticks()
    {
        var def = EffectTestKit.MinimalInfinite("i");
        def.DurationTicks = 5;
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    // Rule 4: Executions only on Instant or when PeriodTicks > 0.

    [Test]
    public void Build_rejects_executions_on_duration_without_period()
    {
        var def = EffectTestKit.MinimalDuration("d", 10);
        def.Executions.Add(new ExecutionDef { CalcTag = "calc.execution.dmg" });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("d"));
    }

    // Rule 5: stack-only policies without stacking (MaxStack == 0).

    [Test]
    public void Build_rejects_add_stack_reapply_without_max_stack()
    {
        var def = EffectTestKit.MinimalDuration("s", 10);
        def.Stack.OnReapply = StackReapply.AddStack;
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("s"));
    }

    [Test]
    public void Build_rejects_apply_effect_overflow_without_max_stack()
    {
        var def = EffectTestKit.MinimalDuration("s2", 10);
        def.Stack.OnOverflow = StackOverflow.ApplyEffect;
        def.Stack.OverflowEffectName = "s2";
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("s2"));
    }

    // Rule: modifiers on Instant/periodic effects allow Add only.

    [Test]
    public void Build_rejects_instant_modifier_with_multiply_op()
    {
        var def = EffectTestKit.MinimalInstant("mul");
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Multiply,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(-3, -1)) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("mul"));
    }

    [Test]
    public void Build_rejects_periodic_duration_modifier_with_override_op()
    {
        var def = EffectTestKit.MinimalDuration("periodic", 100);
        def.PeriodTicks = 10;
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Override,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(0) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("periodic"));
    }

    [Test]
    public void Build_allows_multiply_op_on_non_periodic_duration_modifier()
    {
        var def = EffectTestKit.MinimalDuration("buff", 100);
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Multiply,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, -1)) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.DoesNotThrow(() => EffectTestKit.BuildCatalog(builder));
    }

    // Rule 6: tags, CalcTags, and SelectorTags must resolve in the catalog/SeamRegistry.

    [Test]
    public void Build_rejects_unresolved_granted_tag()
    {
        var def = EffectTestKit.MinimalDuration("d", 5);
        def.GrantedTags.Add("no.such.tag");
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("d"));
    }

    [Test]
    public void Build_rejects_unresolved_magnitude_calc_tag()
    {
        // "calc.magnitude.x" exists in the shared catalog, but EffectTestKit.BuildCatalog's SeamRegistry is empty.
        var def = EffectTestKit.MinimalInstant("i");
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x" },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    [Test]
    public void Build_rejects_unresolved_chain_selector_tag()
    {
        var a = EffectTestKit.MinimalInstant("a");
        var edge = EffectTestKit.Edge(ChainTrigger.OnCompleteNormal, "a");
        edge.SelectorTag = "selector.team";   // in the catalog but not registered in the SeamRegistry
        a.Chains.Add(edge);
        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("a"));
    }

    // Rule 7: Operand attribute references must be registered; OngoingConditions cannot use SourceAttribute.

    [Test]
    public void Build_rejects_operand_referencing_unregistered_attribute()
    {
        var def = EffectTestKit.MinimalInstant("i");
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Attribute(999) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    [Test]
    public void Build_rejects_source_attribute_operand_in_ongoing_conditions()
    {
        var def = EffectTestKit.MinimalDuration("d", 5);
        def.OngoingConditions.Add(new ConditionDef
        {
            Left = Operand.SourceAttribute(EffectTestKit.Hp),
            Op = ComparisonOp.Greater,
            Right = Operand.Constant(0),
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("d"));
    }

    // Rule 8: chain/overflow EffectNames must resolve within the catalog.

    [Test]
    public void Build_rejects_unresolved_chain_effect_name()
    {
        var a = EffectTestKit.MinimalInstant("a");
        a.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "missing"));
        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("a"));
    }

    // Rule 9: MagnitudeDef takes CalcTag XOR Base; PerLevel only with Base.

    [Test]
    public void Build_rejects_magnitude_with_both_calctag_and_base()
    {
        var def = EffectTestKit.MinimalInstant("i");
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x", Base = Operand.Constant(1) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    [Test]
    public void Build_rejects_magnitude_with_neither_calctag_nor_base()
    {
        var def = EffectTestKit.MinimalInstant("i");
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef(),
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    [Test]
    public void Build_rejects_perlevel_without_base()
    {
        var def = EffectTestKit.MinimalInstant("i");
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x", PerLevel = Operand.Constant(1) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    // Rule 10: chain cycles are warnings, not errors.

    [Test]
    public void Application_only_cycle_is_a_high_warning_not_an_error()
    {
        var a = EffectTestKit.MinimalInstant("a");
        a.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "b"));
        var b = EffectTestKit.MinimalInstant("b");
        b.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "a"));
        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        builder.Add(b);
        var catalog = EffectTestKit.BuildCatalog(builder);
        Assert.That(catalog.BuildWarnings, Has.Some.Contains("high"));
    }

    [Test]
    public void Cycle_through_duration_effect_is_a_low_warning()
    {
        var a = EffectTestKit.MinimalDuration("a", 10);
        a.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "b"));
        var b = EffectTestKit.MinimalInstant("b");
        b.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "a"));
        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        builder.Add(b);
        var catalog = EffectTestKit.BuildCatalog(builder);
        Assert.That(catalog.BuildWarnings, Has.Some.Contains("low"));
    }

    // Rule 10b: an OnEvent chain that closes back to a RaiseEvents tag is also a warning (extends the
    // OnApplication cycle detection above using the same IsAncestorOrSelf rule DispatchEvent uses).

    private static EffectDef InstantThatRaises(string name, string eventTag, int hpDelta = -1)
    {
        var def = EffectTestKit.MinimalInstant(name);
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(hpDelta) },
        });
        def.RaiseEvents.Add(new RaiseEventDef { Tag = eventTag, AttributeId = EffectTestKit.Hp });
        return def;
    }

    private static ChainEdgeDef EventEdge(string eventTag, string effectName)
    {
        var edge = EffectTestKit.Edge(ChainTrigger.OnEvent, effectName);
        edge.EventTag = eventTag;
        edge.Listen = ChainListen.AsTarget;
        return edge;
    }

    [Test]
    public void OnEvent_cycle_closing_on_the_same_effect_is_a_warning()
    {
        var a = InstantThatRaises("a", "event.damage.taken");
        a.Chains.Add(EventEdge("event.damage.taken", "a"));

        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        var catalog = EffectTestKit.BuildCatalog(builder);
        Assert.That(catalog.BuildWarnings, Has.Some.Contains("OnEvent chain cycle"));
    }

    [Test]
    public void OnEvent_cycle_across_two_effects_is_a_warning()
    {
        var a = InstantThatRaises("a", "event.damage.taken", hpDelta: -1);
        a.Chains.Add(EventEdge("event.heal.taken", "b"));

        var b = InstantThatRaises("b", "event.heal.taken", hpDelta: 1);
        b.Chains.Add(EventEdge("event.damage.taken", "a"));

        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        builder.Add(b);
        var catalog = EffectTestKit.BuildCatalog(builder);
        Assert.That(catalog.BuildWarnings, Has.Some.Contains("OnEvent chain cycle"));
    }

    [Test]
    public void OnEvent_cycle_closing_only_through_tag_hierarchy_is_a_warning()
    {
        var a = InstantThatRaises("a", "event.damage.taken");
        // "event.damage" is an ancestor of "event.damage.taken" -- an exact-tag comparison would miss this.
        a.Chains.Add(EventEdge("event.damage", "a"));

        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        var catalog = EffectTestKit.BuildCatalog(builder);
        Assert.That(catalog.BuildWarnings, Has.Some.Contains("OnEvent chain cycle"));
    }

    [Test]
    public void OnEvent_listener_with_a_more_specific_tag_than_the_raise_does_not_warn()
    {
        var a = InstantThatRaises("a", "event.damage");
        // "event.damage.taken" is a descendant of the raised tag, not an ancestor -- must not match.
        a.Chains.Add(EventEdge("event.damage.taken", "a"));

        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        var catalog = EffectTestKit.BuildCatalog(builder);
        Assert.That(catalog.BuildWarnings, Has.None.Contains("OnEvent chain cycle"));
    }

    // Misc: name lookup and happy-path compile results.

    [Test]
    public void GetRequiredId_and_TryGetId_resolve_registered_names()
    {
        var builder = new EffectCatalogBuilder();
        builder.Add(EffectTestKit.MinimalInstant("a"));
        var catalog = EffectTestKit.BuildCatalog(builder);

        Assert.That(catalog.GetRequiredId("a"), Is.EqualTo(0));
        Assert.That(catalog.TryGetId("a", out var id), Is.True);
        Assert.That(id, Is.EqualTo(0));
        Assert.That(catalog.TryGetId("missing", out _), Is.False);
        Assert.Throws<KeyNotFoundException>(() => catalog.GetRequiredId("missing"));
    }

    [Test]
    public void Happy_path_compiles_instant_modifier_and_duration_with_granted_tags_and_chain()
    {
        var tags = EffectTestKit.LoadCatalog();
        var seamBuilder = new SeamRegistryBuilder();
        var magCalc = new FixedMagnitude();
        seamBuilder.RegisterMagnitudeCalc(tags.GetRequired("calc.magnitude.x"), magCalc);
        var seams = seamBuilder.Build(tags);
        var attributes = EffectTestKit.BuildAttributeRegistry();

        var bolt = EffectTestKit.MinimalInstant("bolt");
        bolt.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x" },
        });

        var hasted = EffectTestKit.MinimalDuration("hasted", 100);
        hasted.GrantedTags.Add("state.hasted");
        hasted.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "bolt"));

        var builder = new EffectCatalogBuilder();
        builder.Add(bolt);
        builder.Add(hasted);
        var catalog = builder.Build(tags, seams, attributes);

        Assert.That(catalog.Count, Is.EqualTo(2));

        var boltId = catalog.GetRequiredId("bolt");
        var boltEffect = catalog.GetEffect(boltId);
        Assert.That(boltEffect.Modifiers[0].Calc, Is.SameAs(magCalc));
        Assert.That(boltEffect.Modifiers[0].AttributeId, Is.EqualTo(EffectTestKit.Hp));

        var hastedId = catalog.GetRequiredId("hasted");
        var hastedEffect = catalog.GetEffect(hastedId);
        Assert.That(hastedEffect.GrantedTags[0], Is.EqualTo(tags.GetRequired("state.hasted")));
        Assert.That(hastedEffect.Chains[0].EffectId, Is.EqualTo(boltId));
        Assert.That(catalog.BuildWarnings, Is.Empty);
    }

    // The compiled catalog must copy StackPolicyDef's values at Build time rather than holding a
    // reference to the mutable authoring object -- otherwise mutating the authoring def after Build
    // silently mutates the "immutable" compiled catalog.
    [Test]
    public void Build_copies_the_authoring_stack_policy_so_later_mutation_does_not_leak_into_the_compiled_catalog()
    {
        var kit = EffectTestKit.Create();
        var stackPolicy = new StackPolicyDef { MaxStack = 3, OnReapply = StackReapply.AddStack, LevelFromStack = true };
        var def = EffectTestKit.MinimalDuration("leaky", 10);
        def.Stack = stackPolicy;
        kit.AddEffect(def);

        var compiled = kit.Catalog.GetEffect(kit.EffectId("leaky"));

        stackPolicy.MaxStack = 99;
        stackPolicy.OnReapply = StackReapply.ExtendCapped;
        stackPolicy.LevelFromStack = false;

        Assert.That(compiled.Stack.MaxStack, Is.EqualTo(3));
        Assert.That(compiled.Stack.OnReapply, Is.EqualTo(StackReapply.AddStack));
        Assert.That(compiled.Stack.LevelFromStack, Is.True);
    }
}
