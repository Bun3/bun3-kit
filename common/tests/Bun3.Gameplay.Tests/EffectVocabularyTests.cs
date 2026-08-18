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
        kit.AddSpec(lower);

        var higher = EffectTestKit.MinimalDuration("frost_higher", ticks: 100);
        higher.AssetTags.Add("effect.frost");
        higher.RemoveOnApplyTags.Add("effect.frost");
        kit.AddSpec(higher);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.SpecId("frost_lower"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));

        pipeline.EnqueueApply(kit.SpecId("frost_higher"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
        var remaining = kit.Defender.ActiveEffects[0];
        Assert.That(remaining.SpecId, Is.EqualTo(kit.SpecId("frost_higher")));

        var events = kit.Defender.PendingEffectEvents;
        var sawRemovedPrematurely = false;
        for (var i = 0; i < events.Length; i++)
        {
            if (events[i].Kind == EffectLifecycleKind.RemovedPrematurely
                && events[i].SpecId == kit.SpecId("frost_lower"))
            {
                sawRemovedPrematurely = true;
            }
        }

        Assert.That(sawRemovedPrematurely, Is.True);
    }

    [Test]
    public void RemoveOnApplyTags_excludes_same_spec_because_merge_wins()
    {
        var kit = EffectTestKit.Create();
        var buff = EffectTestKit.MinimalDuration("selfstack", ticks: 100);
        buff.AssetTags.Add("effect.frost");
        buff.RemoveOnApplyTags.Add("effect.frost");
        kit.AddSpec(buff);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.SpecId("selfstack"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("selfstack"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // 같은 스펙은 제거 대상에서 제외되고 병합되므로 인스턴스는 하나로 유지된다.
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
    }

    // ---- G2: ChanceToApply ----

    private static EffectSpec ChanceBuff(string name)
    {
        var spec = EffectTestKit.MinimalInstant(name);
        spec.ChanceToApply = new MagnitudeDef { Base = Operand.Attribute(EffectTestKit.Resistance) };
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(10) },
        });
        return spec;
    }

    [Test]
    public void ChanceToApply_zero_never_applies()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(ChanceBuff("buff"));
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Resistance, 0);
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

        pipeline.EnqueueApply(kit.SpecId("buff"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo(BigNum.Zero));
    }

    [Test]
    public void ChanceToApply_one_always_applies()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(ChanceBuff("buff"));
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Resistance, 1);
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

        pipeline.EnqueueApply(kit.SpecId("buff"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)10));
    }

    [Test]
    public void ChanceToApply_is_deterministic_across_identical_runs()
    {
        BigNum RunOnce()
        {
            var kit = EffectTestKit.Create();
            var spec = EffectTestKit.MinimalInstant("roll");
            spec.ChanceToApply = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(5, -1)) }; // 0.5
            spec.Modifiers.Add(new ModifierDef
            {
                AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(1) },
            });
            kit.AddSpec(spec);
            var pipeline = kit.BuildPipeline();
            kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

            for (var i = 0; i < 20; i++)
            {
                pipeline.EnqueueApply(kit.SpecId("roll"), kit.Attacker.Id, kit.Defender.Id);
                pipeline.Tick();
            }

            return kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack);
        }

        Assert.That(RunOnce(), Is.EqualTo(RunOnce()));
    }

    // ---- G4: LevelFromStack ----

    private static EffectSpec LevelFromStackBuff(string name)
    {
        var spec = EffectTestKit.MinimalDuration(name, ticks: 100);
        spec.MaxLevel = 5;
        spec.Stack = new StackPolicy { MaxStack = 5, OnReapply = StackReapply.AddStack, LevelFromStack = true };
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Formula = "x*10" },
            ScaleWithStack = false,   // 스택×배율(기본)과 레벨 재평가가 겹치지 않게 분리 — 레벨 재평가만 관측
        });
        return spec;
    }

    [Test]
    public void LevelFromStack_reevaluates_modifier_magnitude_per_stack()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(LevelFromStackBuff("stacking"));
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

        pipeline.EnqueueApply(kit.SpecId("stacking"), kit.Attacker.Id, kit.Defender.Id, level: 1);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)10)); // 1중첩

        pipeline.EnqueueApply(kit.SpecId("stacking"), kit.Attacker.Id, kit.Defender.Id, level: 1);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)20)); // 2중첩 재평가

        pipeline.EnqueueApply(kit.SpecId("stacking"), kit.Attacker.Id, kit.Defender.Id, level: 1);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)30)); // 3중첩 재평가
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
    }

    [Test]
    public void LevelFromStack_without_max_stack_fails_build()
    {
        var spec = EffectTestKit.MinimalDuration("bad", ticks: 10);
        spec.Stack = new StackPolicy { LevelFromStack = true };
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    // ---- G5: StackReapply.ExtendCapped ----

    private static EffectSpec ExtendCappedBuff(string name) => new EffectSpec
    {
        Name = name,
        DurationType = EffectDurationType.Duration,
        DurationTicks = 10,
        Stack = new StackPolicy { OnReapply = StackReapply.ExtendCapped },
    };

    [Test]
    public void ExtendCapped_extends_remaining_ticks_up_to_pandemic_cap()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(ExtendCappedBuff("dot"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        for (var i = 0; i < 5; i++) pipeline.Tick();   // 5틱 경과 — 남은 5

        var instance = kit.Defender.ActiveEffects[0];
        Assert.That(instance.RemainingTicks, Is.EqualTo(5));

        // 재적용은 병합(①)에서 min(5+10, 10*1.3=13)=13으로 연장되지만, 같은 틱의 ②AdvanceTime이
        // 곧바로 1을 깎으므로 관측 가능한 값은 12다 — 상한이 없었다면 min(5+10)=15가 14로 보였을 것.
        pipeline.EnqueueApply(kit.SpecId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        instance = kit.Defender.ActiveEffects[0];
        Assert.That(instance.RemainingTicks, Is.EqualTo(12));

        // 즉시 재재적용: 병합 min(12+10, 13)=13, 같은 틱 감소로 12 — 상한에 눌려 더 늘지 않는다(판데믹 유지).
        pipeline.EnqueueApply(kit.SpecId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        instance = kit.Defender.ActiveEffects[0];
        Assert.That(instance.RemainingTicks, Is.EqualTo(12));
    }

    [Test]
    public void ExtendCapped_leaves_stack_unchanged()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(ExtendCappedBuff("dot"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffects[0].Stack, Is.EqualTo(1));
    }

    [Test]
    public void ExtendCapped_on_non_duration_fails_build()
    {
        var spec = EffectTestKit.MinimalInfinite("bad");
        spec.Stack = new StackPolicy { OnReapply = StackReapply.ExtendCapped };
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void ExtendCapped_with_multiplier_below_one_fails_build()
    {
        var spec = EffectTestKit.MinimalDuration("bad", ticks: 10);
        spec.Stack = new StackPolicy
        {
            OnReapply = StackReapply.ExtendCapped,
            ExtendCapMultiplier = BigNum.FromParts(9, -1), // 0.9
        };
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    // ---- 라이더: CurveKeys Level > MaxLevel ----

    [Test]
    public void CurveKeys_last_key_exceeding_max_level_fails_build()
    {
        var spec = EffectTestKit.MinimalInstant("bad");
        spec.MaxLevel = 3;
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef
            {
                CurveKeys = new List<LevelKey>
                {
                    new LevelKey { Level = 1, Value = 10 },
                    new LevelKey { Level = 4, Value = 40 },
                },
            },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }
}
