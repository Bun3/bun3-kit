#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectDurationScaleTests
{
    // ---- G3: DurationPerLevel ----

    [Test]
    public void DurationPerLevel_uses_level_specific_duration()
    {
        var kit = EffectTestKit.Create();
        var spec = EffectTestKit.MinimalDuration("scaling_cc", ticks: 0);
        spec.MaxLevel = 3;
        spec.DurationPerLevel = new List<BigNum> { 10, 20, 30 };
        kit.AddSpec(spec);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.SpecId("scaling_cc"), kit.Attacker.Id, kit.Defender.Id, level: 2);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(20));
    }

    [Test]
    public void DurationPerLevel_length_mismatch_fails_build()
    {
        var spec = EffectTestKit.MinimalDuration("bad", ticks: 0);
        spec.MaxLevel = 3;
        spec.DurationPerLevel = new List<BigNum> { 10, 20 };
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DurationPerLevel_without_max_level_fails_build()
    {
        var spec = EffectTestKit.MinimalDuration("bad", ticks: 0);
        spec.DurationPerLevel = new List<BigNum> { 10 };
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DurationPerLevel_with_duration_ticks_fails_build()
    {
        var spec = EffectTestKit.MinimalDuration("bad", ticks: 10);
        spec.MaxLevel = 1;
        spec.DurationPerLevel = new List<BigNum> { 10 };
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DurationPerLevel_on_infinite_fails_build()
    {
        var spec = EffectTestKit.MinimalInfinite("bad");
        spec.MaxLevel = 1;
        spec.DurationPerLevel = new List<BigNum> { 10 };
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    // ---- G3: DurationScale ----

    [Test]
    public void DurationScale_multiplies_duration_once_at_creation()
    {
        var kit = EffectTestKit.Create();
        var spec = EffectTestKit.MinimalDuration("scaled", ticks: 10);
        spec.DurationScale = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, 0)) };   // ×2
        kit.AddSpec(spec);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.SpecId("scaled"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(20));
    }

    [Test]
    public void DurationScale_floors_to_zero_clamps_to_minimum_one_tick_without_dr()
    {
        var kit = EffectTestKit.Create();
        var spec = EffectTestKit.MinimalDuration("tiny", ticks: 10);
        spec.DurationScale = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(5, -2)) };   // ×0.05 -> 0.5 -> floor 0
        kit.AddSpec(spec);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.SpecId("tiny"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(1));
    }

    [Test]
    public void DurationScale_on_instant_fails_build()
    {
        var spec = EffectTestKit.MinimalInstant("bad");
        spec.DurationScale = new MagnitudeDef { Base = Operand.Constant(1) };
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void ExtendCapped_new_duration_uses_duration_scale()
    {
        var kit = EffectTestKit.Create();
        var spec = EffectTestKit.MinimalDuration("dot", ticks: 10);
        spec.DurationScale = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, 0)) };   // ×2 -> 20
        spec.Stack = new StackPolicy { OnReapply = StackReapply.ExtendCapped };
        kit.AddSpec(spec);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.SpecId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(20));

        for (var i = 0; i < 5; i++) pipeline.Tick();   // 15 remaining
        pipeline.EnqueueApply(kit.SpecId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // Merge: min(15+20, 20*1.3=26)=26; same-tick AdvanceTime subtracts 1, so 25 is observed.
        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(25));
    }

    // ---- G6: diminishing returns (DR) ----

    private static EffectSpec DrCcSpec(string name) => new EffectSpec
    {
        Name = name,
        DurationType = EffectDurationType.Duration,
        DurationTicks = 10,
        DrCategory = "effect.frost",
        DrWindowTicks = 100,
        DrStageMultipliers = new List<BigNum> { BigNum.FromParts(5, -1), BigNum.Zero },   // [0.5, 0]
    };

    private static void RunUntilExpired(EffectPipeline pipeline, EffectTarget target)
    {
        for (var i = 0; i < 50 && target.ActiveEffectCount > 0; i++) pipeline.Tick();
        Assert.That(target.ActiveEffectCount, Is.Zero, "test precondition: effect did not expire.");
    }

    [Test]
    public void Dr_stages_reduce_duration_then_immunize_then_reset_after_window()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(DrCcSpec("cc"));
        var pipeline = kit.BuildPipeline();

        // 1st application: multiplier 1 -> duration 10.
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(10));
        RunUntilExpired(pipeline, kit.Defender);

        // 2nd application (after expiry): multiplier 0.5 -> duration 5.
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(5));
        RunUntilExpired(pipeline, kit.Defender);

        // 3rd application: multiplier 0 -> duration 0 -> immune (fizzles), no instance created.
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);

        // 4th application after the window (100 ticks): count resets -> multiplier 1 -> duration 10 again.
        for (var i = 0; i < 101; i++) pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(10));
    }

    [Test]
    public void Dr_window_boundary_is_still_within_window_when_exactly_equal()
    {
        // Pins the window boundary: when lastAppliedTick + DrWindowTicks == current tick, the
        // implementation compares with "<" (strict), so no reset yet — expect multiplier 0.5
        // (duration 5) where a reset would have given 1 (duration 10).
        var kit = EffectTestKit.Create();
        kit.AddSpec(DrCcSpec("cc"));
        var pipeline = kit.BuildPipeline();

        var t0 = pipeline.CurrentTick;
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(10));
        RunUntilExpired(pipeline, kit.Defender);

        while (pipeline.CurrentTick < t0 + 100) pipeline.Tick();
        Assert.That(pipeline.CurrentTick, Is.EqualTo(t0 + 100));

        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(5));
    }

    [Test]
    public void Dr_immune_application_does_not_dispel_via_remove_on_apply_tags()
    {
        // Regression: the DR immunity check must finish before RemoveOnApplyTags (G1) side effects —
        // an immune application dispelling the target's existing (matching) effects would be an
        // observable distortion.
        var kit = EffectTestKit.Create();

        var guard = EffectTestKit.MinimalInfinite("guard");
        guard.AssetTags.Add("state.frozen");
        kit.AddSpec(guard);

        var cc = DrCcSpec("cc");
        cc.RemoveOnApplyTags.Add("state.frozen");
        kit.AddSpec(cc);

        var pipeline = kit.BuildPipeline();

        // Raise the DR count to 2 so the 3rd application is immune (done before granting guard —
        // the 1st/2nd are not immune, so RemoveOnApplyTags fires normally and guard must not exist yet).
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        RunUntilExpired(pipeline, kit.Defender);
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        RunUntilExpired(pipeline, kit.Defender);

        pipeline.EnqueueApply(kit.SpecId("guard"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));

        // 3rd (immune) application — guard must not be dispelled and no cc instance created.
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
        Assert.That(kit.Defender.ActiveEffects[0].SpecId, Is.EqualTo(kit.SpecId("guard")));
    }

    [Test]
    public void Dr_history_round_trips_through_snapshot_restore()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(DrCcSpec("cc"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();   // 1st application: records count 1.
        RunUntilExpired(pipeline, kit.Defender);

        var snapshot = kit.Defender.CreateSnapshot();

        // After the snapshot, apply the 2nd on the original -> multiplier 0.5 -> duration 5 (reference).
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        var referenceTicks = kit.Defender.ActiveEffects[0].RemainingTicks;
        Assert.That(referenceTicks, Is.EqualTo(5));
        RunUntilExpired(pipeline, kit.Defender);

        // Restoring the snapshot and replaying the same 2nd application must give the same DR stage (0.5 -> 5).
        kit.Defender.RestoreSnapshot(snapshot, kit.Catalog);
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffects[0].RemainingTicks, Is.EqualTo(referenceTicks));
    }

    [Test]
    public void DrCategory_requires_window_ticks_at_least_one()
    {
        var spec = DrCcSpec("bad");
        spec.DrWindowTicks = 0;
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DrCategory_requires_non_empty_stage_multipliers()
    {
        var spec = DrCcSpec("bad");
        spec.DrStageMultipliers = new List<BigNum>();
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DrCategory_rejects_negative_stage_multiplier()
    {
        var spec = DrCcSpec("bad");
        spec.DrStageMultipliers = new List<BigNum> { BigNum.FromParts(-1, 0) };
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DrCategory_on_infinite_fails_build()
    {
        var spec = new EffectSpec
        {
            Name = "bad",
            DurationType = EffectDurationType.Infinite,
            DrCategory = "effect.frost",
            DrWindowTicks = 100,
            DrStageMultipliers = new List<BigNum> { BigNum.FromParts(5, -1) },
        };
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    // ---- Rider: LevelFromStack x ScaleWithStack combination warning ----

    [Test]
    public void LevelFromStack_with_scale_with_stack_modifier_emits_build_warning()
    {
        var spec = EffectTestKit.MinimalDuration("stacked_curve", ticks: 10);
        spec.MaxLevel = 3;
        spec.Stack = new StackPolicy { MaxStack = 3, OnReapply = StackReapply.AddStack, LevelFromStack = true };
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Formula = "x*10" },
            // ScaleWithStack stays default true — the warning trigger condition.
        });

        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var catalog = EffectTestKit.BuildCatalog(builder);

        var sawWarning = false;
        for (var i = 0; i < catalog.BuildWarnings.Count; i++)
        {
            if (catalog.BuildWarnings[i].Contains("stacked_curve")) sawWarning = true;
        }

        Assert.That(sawWarning, Is.True);
    }

    [Test]
    public void LevelFromStack_without_scale_with_stack_modifier_emits_no_warning()
    {
        var spec = EffectTestKit.MinimalDuration("stacked_curve_clean", ticks: 10);
        spec.MaxLevel = 3;
        spec.Stack = new StackPolicy { MaxStack = 3, OnReapply = StackReapply.AddStack, LevelFromStack = true };
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Formula = "x*10" },
            ScaleWithStack = false,
        });

        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var catalog = EffectTestKit.BuildCatalog(builder);

        Assert.That(catalog.BuildWarnings, Is.Empty);
    }
}
