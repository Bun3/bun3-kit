#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectVocabularyTests
{
    // ---- G1: RemoveOnApplyTags ----

    [Test]
    public void RemoveOnApplyTags_replaces_lower_tier_with_higher_tier()
    {
        var kit = EffectTestKit.Create();
        var lower = EffectTestKit.MinimalDuration("frost_lower", ticks: 100);
        lower.AssetTags.Add("effect.frost");
        kit.AddEffect(lower);

        var higher = EffectTestKit.MinimalDuration("frost_higher", ticks: 100);
        higher.AssetTags.Add("effect.frost");
        higher.RemoveOnApplyTags.Add("effect.frost");
        kit.AddEffect(higher);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.EffectId("frost_lower"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));

        pipeline.EnqueueApply(kit.EffectId("frost_higher"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
        var remaining = kit.Defender.ActiveEffects[0];
        Assert.That(remaining.EffectId, Is.EqualTo(kit.EffectId("frost_higher")));

        var events = pipeline.PendingEffectEvents;
        var sawRemovedPrematurely = false;
        for (var i = 0; i < events.Length; i++)
        {
            if (events[i].Kind == EffectLifecycleKind.RemovedPrematurely
                && events[i].EffectId == kit.EffectId("frost_lower"))
            {
                Assert.That(events[i].Source, Is.EqualTo(kit.Attacker.Id));
                Assert.That(events[i].Target, Is.EqualTo(kit.Defender.Id));
                Assert.That(events[i].Level, Is.EqualTo(1));
                sawRemovedPrematurely = true;
            }
        }

        Assert.That(sawRemovedPrematurely, Is.True);
    }

    [Test]
    public void RemoveOnApplyTags_excludes_same_effect_because_merge_wins()
    {
        var kit = EffectTestKit.Create();
        var buff = EffectTestKit.MinimalDuration("selfstack", ticks: 100);
        buff.AssetTags.Add("effect.frost");
        buff.RemoveOnApplyTags.Add("effect.frost");
        kit.AddEffect(buff);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.EffectId("selfstack"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("selfstack"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // The same def is excluded from removal and merged, so one instance remains.
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
    }

    // ---- G2: ChanceToApply ----

    private static EffectDef ChanceBuff(string name)
    {
        var def = EffectTestKit.MinimalInstant(name);
        def.ChanceToApply = new MagnitudeDef { Base = Operand.Attribute(EffectTestKit.Resistance) };
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(10) },
        });
        return def;
    }

    [Test]
    public void ChanceToApply_zero_never_applies()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(ChanceBuff("buff"));
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Resistance, 0);
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

        pipeline.EnqueueApply(kit.EffectId("buff"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo(BigNum.Zero));
    }

    [Test]
    public void ChanceToApply_one_always_applies()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(ChanceBuff("buff"));
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Resistance, 1);
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

        pipeline.EnqueueApply(kit.EffectId("buff"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)10));
    }

    [Test]
    public void ChanceToApply_is_deterministic_across_identical_runs()
    {
        BigNum RunOnce()
        {
            var kit = EffectTestKit.Create();
            var def = EffectTestKit.MinimalInstant("roll");
            def.ChanceToApply = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(5, -1)) }; // 0.5
            def.Modifiers.Add(new ModifierDef
            {
                AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(1) },
            });
            kit.AddEffect(def);
            var pipeline = kit.BuildPipeline();
            kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

            for (var i = 0; i < 20; i++)
            {
                pipeline.EnqueueApply(kit.EffectId("roll"), kit.Attacker.Id, kit.Defender.Id);
                pipeline.Tick();
            }

            return kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack);
        }

        Assert.That(RunOnce(), Is.EqualTo(RunOnce()));
    }

    // ---- G4: LevelFromStack ----

    private static EffectDef LevelFromStackBuff(string name)
    {
        var def = EffectTestKit.MinimalDuration(name, ticks: 100);
        def.MaxLevel = 5;
        def.Stack = new StackPolicyDef { MaxStack = 5, OnReapply = StackReapply.AddStack, LevelFromStack = true };
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Formula = "x*10" },
            ScaleWithStack = false,   // keep stack scaling (default) out of the way — observe level re-evaluation only
        });
        return def;
    }

    [Test]
    public void LevelFromStack_reevaluates_modifier_magnitude_per_stack()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(LevelFromStackBuff("stacking"));
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

        pipeline.EnqueueApply(kit.EffectId("stacking"), kit.Attacker.Id, kit.Defender.Id, level: 1);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)10)); // 1 stack

        pipeline.EnqueueApply(kit.EffectId("stacking"), kit.Attacker.Id, kit.Defender.Id, level: 1);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)20)); // re-evaluated at 2 stacks

        pipeline.EnqueueApply(kit.EffectId("stacking"), kit.Attacker.Id, kit.Defender.Id, level: 1);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)30)); // re-evaluated at 3 stacks
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
    }

    [Test]
    public void LevelFromStack_without_max_stack_fails_build()
    {
        var def = EffectTestKit.MinimalDuration("bad", ticks: 10);
        def.Stack = new StackPolicyDef { LevelFromStack = true };
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    // ---- G5: StackReapply.ExtendCapped ----

    private static EffectDef ExtendCappedBuff(string name) => new EffectDef
    {
        Name = name,
        DurationType = EffectDurationType.Duration,
        DurationTicks = 10,
        Stack = new StackPolicyDef { OnReapply = StackReapply.ExtendCapped },
    };

    [Test]
    public void ExtendCapped_extends_remaining_ticks_up_to_pandemic_cap()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(ExtendCappedBuff("dot"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        for (var i = 0; i < 5; i++) pipeline.Tick();   // 5 ticks elapsed — 5 remain

        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(5));

        // Reapply extends in merge (①) to min(5+10, 10*1.3=13)=13, but ② AdvanceTime in the same
        // tick immediately subtracts 1, so the observed value is 12 — without the cap, min(5+10)=15 would show as 14.
        pipeline.EnqueueApply(kit.EffectId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(12));

        // Immediate reapply again: merge min(12+10, 13)=13, minus the same-tick decrement = 12 — pinned by the cap (pandemic behavior).
        pipeline.EnqueueApply(kit.EffectId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(12));
    }

    [Test]
    public void ExtendCapped_leaves_stack_unchanged()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(ExtendCappedBuff("dot"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffects[0].Stack, Is.EqualTo(1));
    }

    [Test]
    public void ExtendCapped_on_non_duration_fails_build()
    {
        var def = EffectTestKit.MinimalInfinite("bad");
        def.Stack = new StackPolicyDef { OnReapply = StackReapply.ExtendCapped };
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void ExtendCapped_with_multiplier_below_one_fails_build()
    {
        var def = EffectTestKit.MinimalDuration("bad", ticks: 10);
        def.Stack = new StackPolicyDef
        {
            OnReapply = StackReapply.ExtendCapped,
            ExtendCapMultiplier = BigNum.FromParts(9, -1), // 0.9
        };
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    // ---- Rider: CurveKeys Level > MaxLevel ----

    [Test]
    public void CurveKeys_last_key_exceeding_max_level_fails_build()
    {
        var def = EffectTestKit.MinimalInstant("bad");
        def.MaxLevel = 3;
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef
            {
                CurveKeys = new List<LevelKeyDef>
                {
                    new LevelKeyDef { Level = 1, Value = 10 },
                    new LevelKeyDef { Level = 4, Value = 40 },
                },
            },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }
}
