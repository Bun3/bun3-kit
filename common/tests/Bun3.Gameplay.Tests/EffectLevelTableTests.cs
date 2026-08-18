#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectLevelTableTests
{
    private static EffectSpec FormulaBuff(string name, LevelTail tail = LevelTail.Clamp, BigNum increment = default)
    {
        var spec = EffectTestKit.MinimalDuration(name, ticks: 100);
        spec.MaxLevel = 5;
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Formula = "x*x", Tail = tail, ExtrapolateIncrement = increment },
        });
        return spec;
    }

    [Test]
    public void Formula_level_table_applies_precomputed_value_at_level()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(FormulaBuff("buff"));
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

        pipeline.EnqueueApply(kit.SpecId("buff"), kit.Attacker.Id, kit.Defender.Id, level: 3);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)9)); // 3*3
    }

    [Test]
    public void Formula_level_table_clamps_beyond_max_level_by_default()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(FormulaBuff("buff"));
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

        pipeline.EnqueueApply(kit.SpecId("buff"), kit.Attacker.Id, kit.Defender.Id, level: 7);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)25)); // 5*5, clamped
    }

    [Test]
    public void Formula_level_table_extrapolates_with_auto_increment_beyond_max_level()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(FormulaBuff("buff", LevelTail.Extrapolate)); // increment auto = 25-16 = 9
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

        pipeline.EnqueueApply(kit.SpecId("buff"), kit.Attacker.Id, kit.Defender.Id, level: 7);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)43)); // 25+(25-16)*2
    }

    [Test]
    public void Chain_fixed_level_exceeding_target_max_level_fails_build()
    {
        var kit = EffectTestKit.Create();
        var target = FormulaBuff("target"); // MaxLevel = 5
        kit.AddSpec(target);

        var source = EffectTestKit.MinimalInstant("source");
        source.Chains.Add(new ChainEdgeDef
        {
            Trigger = ChainTrigger.OnApplication, EffectName = "target",
            LevelRule = ChainLevelRule.Fixed, FixedLevel = 6,
        });
        kit.AddSpec(source);

        Assert.Throws<InvalidOperationException>(() => kit.BuildPipeline());
    }

    [Test]
    public void Chain_fixed_level_within_target_max_level_builds_fine()
    {
        var kit = EffectTestKit.Create();
        var target = FormulaBuff("target"); // MaxLevel = 5
        kit.AddSpec(target);

        var source = EffectTestKit.MinimalInstant("source");
        source.Chains.Add(new ChainEdgeDef
        {
            Trigger = ChainTrigger.OnApplication, EffectName = "target",
            LevelRule = ChainLevelRule.Fixed, FixedLevel = 5,
        });
        kit.AddSpec(source);

        Assert.DoesNotThrow(() => kit.BuildPipeline());
    }

    [Test]
    public void PerLevelValues_length_mismatch_with_max_level_fails_build()
    {
        var spec = EffectTestKit.MinimalInstant("bad");
        spec.MaxLevel = 3;
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { PerLevelValues = new System.Collections.Generic.List<BigNum> { 1, 2 } },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void Level_table_without_max_level_declared_fails_build()
    {
        var spec = EffectTestKit.MinimalInstant("bad");
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Formula = "x" },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void CurveKeys_not_starting_at_level_one_fails_build()
    {
        var spec = EffectTestKit.MinimalInstant("bad");
        spec.MaxLevel = 5;
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef
            {
                CurveKeys = new System.Collections.Generic.List<LevelKey>
                {
                    new LevelKey { Level = 2, Value = 10 },
                },
            },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void CurveKeys_interpolates_linearly_between_keys()
    {
        var spec = EffectTestKit.MinimalDuration("curve", ticks: 100);
        spec.MaxLevel = 5;
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef
            {
                CurveKeys = new System.Collections.Generic.List<LevelKey>
                {
                    new LevelKey { Level = 1, Value = 10 },
                    new LevelKey { Level = 5, Value = 50 },
                },
            },
        });

        var kit = EffectTestKit.Create();
        kit.AddSpec(spec);
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);

        pipeline.EnqueueApply(kit.SpecId("curve"), kit.Attacker.Id, kit.Defender.Id, level: 3);
        pipeline.Tick();

        // 10 + (50-10) * (3-1)/(5-1) = 10 + 20 = 30
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)30));
    }
}
