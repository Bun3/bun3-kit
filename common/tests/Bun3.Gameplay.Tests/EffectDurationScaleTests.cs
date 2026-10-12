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
    [Test]
    public void Duration_ticks_keep_the_scaled_total_while_remaining_counts_down()
    {
        var kit = EffectTestKit.Create();
        var def = EffectTestKit.MinimalDuration("slow", ticks: 10);
        def.DurationScale = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, 0)) };
        kit.AddEffect(def);
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("slow"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.Tick();

        var info = kit.Defender.GetActiveEffect(0);
        Assert.That(info.DurationTicks, Is.EqualTo(20));
        Assert.That(info.RemainingTicks, Is.EqualTo(19));
        Assert.That(info.EffectId, Is.EqualTo(kit.EffectId("slow")));
        Assert.That(info.Source, Is.EqualTo(kit.Attacker.Id));
        Assert.That(kit.Defender.TryGetActiveEffect(info.InstanceId, out var byId), Is.True);
        Assert.That(byId.DurationTicks, Is.EqualTo(20));
        Assert.That(kit.Defender.TryGetActiveEffect(info.InstanceId + 1000, out _), Is.False);
    }

    [Test]
    public void Extending_a_duration_sets_the_total_to_the_new_remaining()
    {
        var kit = EffectTestKit.Create();
        var def = EffectTestKit.MinimalDuration("slow", ticks: 10);
        def.Stack.OnReapply = StackReapply.ExtendCapped;
        kit.AddEffect(def);
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("slow"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        for (var i = 0; i < 4; i++) pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("slow"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // remaining 6 + new 10 = 16, capped at floor(10 x 1.3) = 13; the merge drains before the same
        // tick's time phase, so remaining shows 12 while the total stays at the new lifetime's 13.
        var info = kit.Defender.GetActiveEffect(0);
        Assert.That(info.RemainingTicks, Is.EqualTo(12));
        Assert.That(info.DurationTicks, Is.EqualTo(13));
    }

    // ---- G3: DurationPerLevel ----

    [Test]
    public void DurationPerLevel_uses_level_specific_duration()
    {
        var kit = EffectTestKit.Create();
        var def = EffectTestKit.MinimalDuration("scaling_cc", ticks: 0);
        def.MaxLevel = 3;
        def.DurationPerLevel = new List<BigNum> { 10, 20, 30 };
        kit.AddEffect(def);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.EffectId("scaling_cc"), kit.Attacker.Id, kit.Defender.Id, level: 2);
        pipeline.Tick();

        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(20));
    }

    [Test]
    public void DurationPerLevel_length_mismatch_fails_build()
    {
        var def = EffectTestKit.MinimalDuration("bad", ticks: 0);
        def.MaxLevel = 3;
        def.DurationPerLevel = new List<BigNum> { 10, 20 };
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DurationPerLevel_without_max_level_fails_build()
    {
        var def = EffectTestKit.MinimalDuration("bad", ticks: 0);
        def.DurationPerLevel = new List<BigNum> { 10 };
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DurationPerLevel_with_duration_ticks_fails_build()
    {
        var def = EffectTestKit.MinimalDuration("bad", ticks: 10);
        def.MaxLevel = 1;
        def.DurationPerLevel = new List<BigNum> { 10 };
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DurationPerLevel_on_infinite_fails_build()
    {
        var def = EffectTestKit.MinimalInfinite("bad");
        def.MaxLevel = 1;
        def.DurationPerLevel = new List<BigNum> { 10 };
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    // ---- G3: DurationScale ----

    [Test]
    public void DurationScale_multiplies_duration_once_at_creation()
    {
        var kit = EffectTestKit.Create();
        var def = EffectTestKit.MinimalDuration("scaled", ticks: 10);
        def.DurationScale = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, 0)) };   // ×2
        kit.AddEffect(def);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.EffectId("scaled"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(20));
    }

    [Test]
    public void DurationScale_floors_to_zero_clamps_to_minimum_one_tick_without_dr()
    {
        var kit = EffectTestKit.Create();
        var def = EffectTestKit.MinimalDuration("tiny", ticks: 10);
        def.DurationScale = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(5, -2)) };   // ×0.05 -> 0.5 -> floor 0
        kit.AddEffect(def);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.EffectId("tiny"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(1));
    }

    [Test]
    public void DurationScale_on_instant_fails_build()
    {
        var def = EffectTestKit.MinimalInstant("bad");
        def.DurationScale = new MagnitudeDef { Base = Operand.Constant(1) };
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void ExtendCapped_new_duration_uses_duration_scale()
    {
        var kit = EffectTestKit.Create();
        var def = EffectTestKit.MinimalDuration("dot", ticks: 10);
        def.DurationScale = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, 0)) };   // ×2 -> 20
        def.Stack = new StackPolicyDef { OnReapply = StackReapply.ExtendCapped };
        kit.AddEffect(def);

        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.EffectId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(20));

        for (var i = 0; i < 5; i++) pipeline.Tick();   // 15 remaining
        pipeline.EnqueueApply(kit.EffectId("dot"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // Merge: min(15+20, 20*1.3=26)=26; same-tick AdvanceTime subtracts 1, so 25 is observed.
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(25));
    }

    // ---- G6: diminishing returns (DR) ----

    private static EffectDef DrCcDef(string name) => new EffectDef
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
        kit.AddEffect(DrCcDef("cc"));
        var pipeline = kit.BuildPipeline();

        // 1st application: multiplier 1 -> duration 10.
        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(10));
        RunUntilExpired(pipeline, kit.Defender);

        // 2nd application (after expiry): multiplier 0.5 -> duration 5.
        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(5));
        RunUntilExpired(pipeline, kit.Defender);

        // 3rd application: multiplier 0 -> duration 0 -> immune (fizzles), no instance created.
        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);

        // 4th application after the window (100 ticks): count resets -> multiplier 1 -> duration 10 again.
        for (var i = 0; i < 101; i++) pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(10));
    }

    [Test]
    public void Dr_window_boundary_is_still_within_window_when_exactly_equal()
    {
        // Pins the window boundary: when lastAppliedTick + DrWindowTicks == current tick, the
        // implementation compares with "<" (strict), so no reset yet — expect multiplier 0.5
        // (duration 5) where a reset would have given 1 (duration 10).
        var kit = EffectTestKit.Create();
        kit.AddEffect(DrCcDef("cc"));
        var pipeline = kit.BuildPipeline();

        var t0 = pipeline.CurrentTick;
        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(10));
        RunUntilExpired(pipeline, kit.Defender);

        while (pipeline.CurrentTick < t0 + 100) pipeline.Tick();
        Assert.That(pipeline.CurrentTick, Is.EqualTo(t0 + 100));

        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(5));
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
        kit.AddEffect(guard);

        var cc = DrCcDef("cc");
        cc.RemoveOnApplyTags.Add("state.frozen");
        kit.AddEffect(cc);

        var pipeline = kit.BuildPipeline();

        // Raise the DR count to 2 so the 3rd application is immune (done before granting guard —
        // the 1st/2nd are not immune, so RemoveOnApplyTags fires normally and guard must not exist yet).
        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        RunUntilExpired(pipeline, kit.Defender);
        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        RunUntilExpired(pipeline, kit.Defender);

        pipeline.EnqueueApply(kit.EffectId("guard"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));

        // 3rd (immune) application — guard must not be dispelled and no cc instance created.
        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
        Assert.That(kit.Defender.ActiveEffects[0].EffectId, Is.EqualTo(kit.EffectId("guard")));
    }

    [Test]
    public void Dr_history_round_trips_through_snapshot_restore()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(DrCcDef("cc"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();   // 1st application: records count 1.
        RunUntilExpired(pipeline, kit.Defender);

        var snapshot = kit.Defender.CreateSnapshot();

        // After the snapshot, apply the 2nd on the original -> multiplier 0.5 -> duration 5 (reference).
        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        var referenceTicks = kit.Defender.GetActiveEffect(0).RemainingTicks;
        Assert.That(referenceTicks, Is.EqualTo(5));
        RunUntilExpired(pipeline, kit.Defender);

        // Restoring the snapshot and replaying the same 2nd application must give the same DR stage (0.5 -> 5).
        kit.Defender.RestoreSnapshot(snapshot, kit.Catalog);
        pipeline.EnqueueApply(kit.EffectId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(referenceTicks));
    }

    [Test]
    public void DrCategory_requires_window_ticks_at_least_one()
    {
        var def = DrCcDef("bad");
        def.DrWindowTicks = 0;
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DrCategory_requires_non_empty_stage_multipliers()
    {
        var def = DrCcDef("bad");
        def.DrStageMultipliers = new List<BigNum>();
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DrCategory_rejects_negative_stage_multiplier()
    {
        var def = DrCcDef("bad");
        def.DrStageMultipliers = new List<BigNum> { BigNum.FromParts(-1, 0) };
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void DrCategory_on_infinite_fails_build()
    {
        var def = new EffectDef
        {
            Name = "bad",
            DurationType = EffectDurationType.Infinite,
            DrCategory = "effect.frost",
            DrWindowTicks = 100,
            DrStageMultipliers = new List<BigNum> { BigNum.FromParts(5, -1) },
        };
        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    // ---- Rider: LevelFromStack x ScaleWithStack combination warning ----

    [Test]
    public void LevelFromStack_with_scale_with_stack_modifier_emits_build_warning()
    {
        var def = EffectTestKit.MinimalDuration("stacked_curve", ticks: 10);
        def.MaxLevel = 3;
        def.Stack = new StackPolicyDef { MaxStack = 3, OnReapply = StackReapply.AddStack, LevelFromStack = true };
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Formula = "x*10" },
            // ScaleWithStack stays default true — the warning trigger condition.
        });

        var builder = new EffectCatalogBuilder();
        builder.Add(def);
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
        var def = EffectTestKit.MinimalDuration("stacked_curve_clean", ticks: 10);
        def.MaxLevel = 3;
        def.Stack = new StackPolicyDef { MaxStack = 3, OnReapply = StackReapply.AddStack, LevelFromStack = true };
        def.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Formula = "x*10" },
            ScaleWithStack = false,
        });

        var builder = new EffectCatalogBuilder();
        builder.Add(def);
        var catalog = EffectTestKit.BuildCatalog(builder);

        Assert.That(catalog.BuildWarnings, Is.Empty);
    }
}
