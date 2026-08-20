#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectDeterminismTests
{
    [Test]
    public void Scenario_hash_is_stable_across_runs()
    {
        var first = EffectScenario.Run(20260817, 200);
        var second = EffectScenario.Run(20260817, 200);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void Snapshot_restore_resumes_bit_identically()
    {
        var world = EffectScenario.BuildWorld(20260818);
        EffectScenario.RunTicks(world, 100);

        // Design premise: the apply queue must be empty at snapshot time — OnCompleteNormal chains
        // drain on the next tick, so drain extra ticks if we landed on a boundary.
        while (world.Pipeline.PendingApplyCount > 0) world.Pipeline.Tick();
        Assert.That(world.Pipeline.PendingApplyCount, Is.Zero);

        // XorShiftRng is a class, so assignment shares the reference — Clone() detaches this point's
        // state so restoring world.DriverRng below actually rewinds it.
        var driverRngAtSnapshot = world.DriverRng.Clone();
        var nextInstanceId = world.Pipeline.NextInstanceId;
        var currentTick = world.Pipeline.CurrentTick;
        var snapshots = new EffectTargetSnapshot[world.Targets.Length];
        for (var i = 0; i < world.Targets.Length; i++)
            snapshots[i] = world.Targets[i].CreateSnapshot();

        EffectScenario.RunTicks(world, 100);
        var hashA = EffectScenario.HashState(world.Targets);

        // Restore: targets roll back to snapshots; the pipeline becomes a fresh instance over the
        // same catalog/resolver with only counters rewound — the pending queue is outside snapshot
        // scope, so a fresh instance clears it naturally.
        for (var i = 0; i < world.Targets.Length; i++)
            world.Targets[i].RestoreSnapshot(snapshots[i], world.Catalog);

        world.DriverRng = driverRngAtSnapshot;
        world.Pipeline = new EffectPipeline(world.Catalog, world.Resolver, new XorShiftRng(1))
        {
            NextInstanceId = nextInstanceId,
            CurrentTick = currentTick,
        };

        EffectScenario.RunTicks(world, 100);
        var hashB = EffectScenario.HashState(world.Targets);

        Assert.That(hashB, Is.EqualTo(hashA));
    }

    [Test]
    public void Apply_remove_roundtrip_leaves_no_trace()
    {
        var kit = EffectTestKit.Create();
        const int effectCount = 20;
        for (var i = 0; i < effectCount; i++)
        {
            var def = EffectTestKit.MinimalDuration($"buff{i}", ticks: 5 + i % 15);
            var attributeId = i % 2 == 0 ? EffectTestKit.Attack : EffectTestKit.Hp;
            def.Modifiers.Add(new ModifierDef
            {
                AttributeId = attributeId,
                Op = i % 3 == 0 ? AttributeModifierOp.Multiply : AttributeModifierOp.Add,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(i % 5 + 1) },
            });
            kit.AddEffect(def);
        }

        var pipeline = kit.BuildPipeline();
        kit.Attacker.Attributes.SetBase(EffectTestKit.MaxHp, 500);
        kit.Attacker.Attributes.SetBase(EffectTestKit.Hp, 500);
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 500);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 500);

        var referenceAttackerHp = kit.Attacker.Attributes.GetCurrent(EffectTestKit.Hp);
        var referenceAttackerAttack = kit.Attacker.Attributes.GetCurrent(EffectTestKit.Attack);
        var referenceDefenderHp = kit.Defender.Attributes.GetCurrent(EffectTestKit.Hp);
        var referenceDefenderAttack = kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack);

        var rng = new XorShiftRng(20260819);
        for (var op = 0; op < 500; op++)
        {
            var target = rng.NextUInt32() % 2 == 0 ? kit.Attacker : kit.Defender;
            var roll = rng.NextUInt32() % 3;
            if (roll != 1 || target.ActiveEffectCount == 0)
            {
                var effectId = kit.EffectId($"buff{rng.NextUInt32() % effectCount}");
                pipeline.EnqueueApply(effectId, kit.Attacker.Id, target.Id);
            }
            else
            {
                var index = (int)(rng.NextUInt32() % (uint)target.ActiveEffectCount);
                pipeline.RemoveById(target.Id, target.ActiveEffects[index].Id);
            }

            pipeline.Tick();
        }

        RemoveAllActive(pipeline, kit.Attacker);
        RemoveAllActive(pipeline, kit.Defender);
        kit.Attacker.Attributes.RebuildDirty();
        kit.Defender.Attributes.RebuildDirty();

        Assert.That(kit.Attacker.Attributes.GetCurrent(EffectTestKit.Hp), Is.EqualTo(referenceAttackerHp));
        Assert.That(kit.Attacker.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo(referenceAttackerAttack));
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Hp), Is.EqualTo(referenceDefenderHp));
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo(referenceDefenderAttack));
    }

    [Test]
    public void Restore_snapshot_respects_enabled_when_handling_granted_tags()
    {
        var kit = EffectTestKit.Create();
        var hastedTag = kit.Tag("state.hasted");

        // Ongoing effect that toggles off immediately when its condition (Mp<50) is false — grants the tag but disabled.
        var ongoingHaste = EffectTestKit.MinimalInfinite("ongoingHaste");
        ongoingHaste.OngoingConditions.Add(new ConditionDef
        {
            Left = Operand.Attribute(EffectTestKit.Mp),
            Op = ComparisonOp.Less,
            Right = Operand.Constant(50),
        });
        ongoingHaste.GrantedTags.Add("state.hasted");
        kit.AddEffect(ongoingHaste);

        // Separate unconditional effect granting the same tag — the "other active instance" on Defender.
        var permaHaste = EffectTestKit.MinimalInfinite("permaHaste");
        permaHaste.GrantedTags.Add("state.hasted");
        kit.AddEffect(permaHaste);

        var pipeline = kit.BuildPipeline();
        kit.Attacker.Attributes.SetBase(EffectTestKit.Mp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Mp, 100);

        // Attacker: ongoingHaste alone — toggled off, so no tag should remain.
        pipeline.EnqueueApply(kit.EffectId("ongoingHaste"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.Tick();
        Assert.That(kit.Attacker.ActiveEffects[0].Enabled, Is.False);   // precondition: toggled off
        Assert.That(kit.Attacker.Tags.Has(hastedTag), Is.False);

        // Defender: ongoingHaste (disabled) + permaHaste (active) both grant the same tag.
        pipeline.EnqueueApply(kit.EffectId("ongoingHaste"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("permaHaste"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Tags.Count(hastedTag), Is.EqualTo(1));   // precondition: only permaHaste contributes

        var attackerSnapshot = kit.Attacker.CreateSnapshot();
        var defenderSnapshot = kit.Defender.CreateSnapshot();

        kit.Attacker.RestoreSnapshot(attackerSnapshot, kit.Catalog);
        kit.Defender.RestoreSnapshot(defenderSnapshot, kit.Catalog);

        // Right after restore: disabled instances grant no tags (1); active instance counts are preserved (2).
        Assert.That(kit.Attacker.Tags.Has(hastedTag), Is.False);
        Assert.That(kit.Defender.Tags.Count(hastedTag), Is.EqualTo(1));

        pipeline.Tick();   // still holds one tick later

        Assert.That(kit.Attacker.Tags.Has(hastedTag), Is.False);
        Assert.That(kit.Defender.Tags.Count(hastedTag), Is.EqualTo(1));
    }

    private static void RemoveAllActive(EffectPipeline pipeline, EffectTarget target)
    {
        while (target.ActiveEffectCount > 0)
            pipeline.RemoveById(target.Id, target.ActiveEffects[0].Id);
    }

    [Test]
    public void Same_seed_produces_the_same_world_state_hash()
    {
        var kit = EffectScenario.ScenarioKit();
        var first = EffectScenario.RunHash(kit.Effects, kit.Abilities, kit.Tags, seed: 20260820);
        var second = EffectScenario.RunHash(kit.Effects, kit.Abilities, kit.Tags, seed: 20260820);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void Reflection_round_trip_leaves_no_residue()
    {
        // A reflects onto B and B reflects onto A; after the exchange settles, removing both
        // reflect effects must return every attribute (Base and Current) to its starting value.
        // Reflect is a modifier-only Duration/Infinite effect, so Base alone would pass even if
        // removal left the modifier attached — Current is what actually proves it is gone.
        var kit = EffectScenario.ScenarioKit();
        var before = EffectScenario.CaptureAttributes(kit);
        EffectScenario.RunMutualReflection(kit, ticks: 30);
        EffectScenario.RemoveAllEffects(kit);
        for (var i = 0; i < 3; i++) kit.World.Tick();

        Assert.That(EffectScenario.CaptureAttributes(kit), Is.EqualTo(before));
    }

    [Test]
    public void Grant_and_revoke_round_trip_restores_the_ability_set()
    {
        var kit = EffectScenario.ScenarioKit();
        var before = EffectScenario.CaptureGrants(kit);
        EffectScenario.ApplyGrantingEffectsAndRemoveThem(kit);

        Assert.That(EffectScenario.CaptureGrants(kit), Is.EqualTo(before));
    }

    [Test]
    public void Settled_tick_loop_does_not_allocate()
    {
        var kit = EffectTestKit.Create();
        var buff = EffectTestKit.MinimalInfinite("buff");
        buff.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(5) },
        });
        kit.AddEffect(buff);

        var poison = EffectTestKit.MinimalDuration("poison", ticks: 2000);
        poison.PeriodTicks = 1;
        poison.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-1) },
        });
        kit.AddEffect(poison);

        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 50);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 50);

        pipeline.EnqueueApply(kit.EffectId("buff"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.EnqueueApply(kit.EffectId("poison"), kit.Attacker.Id, kit.Defender.Id);

        // Warm-up — tick until Hp bottoms out at 0 and Current no longer changes,
        // growing event/change buffers to their peak capacity along the way.
        for (var i = 0; i < 60; i++)
        {
            pipeline.Tick();
            kit.Defender.Attributes.ClearChanges();
            kit.Defender.ClearEffectEvents();
        }

        Assert.That(pipeline.PendingApplyCount, Is.Zero);
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Hp), Is.EqualTo((BigNum)0));

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            pipeline.Tick();
            kit.Defender.Attributes.ClearChanges();
            kit.Defender.ClearEffectEvents();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, "settled Tick() loop allocated on the heap");
    }
}
