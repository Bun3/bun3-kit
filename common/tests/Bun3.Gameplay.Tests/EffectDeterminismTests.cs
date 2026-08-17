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

        // 스냅샷 시점 큐는 비어 있어야 한다는 설계 전제 — 만감/폭탄 체인의 OnCompleteNormal은
        // 다음 틱에서 드레인되므로, 경계에 걸렸다면 여분 틱으로 마저 드레인해 전제를 맞춘다.
        while (world.Pipeline.PendingApplyCount > 0) world.Pipeline.Tick();
        Assert.That(world.Pipeline.PendingApplyCount, Is.Zero);

        var driverRngAtSnapshot = world.DriverRng;
        var nextInstanceId = world.Pipeline.NextInstanceId;
        var currentTick = world.Pipeline.CurrentTick;
        var snapshots = new EffectTargetSnapshot[world.Targets.Length];
        for (var i = 0; i < world.Targets.Length; i++)
            snapshots[i] = world.Targets[i].CreateSnapshot();

        EffectScenario.RunTicks(world, 100);
        var hashA = EffectScenario.HashState(world.Targets);

        // 복원: 대상은 스냅샷으로 되돌리고, 파이프라인은 같은 카탈로그·리졸버의 새 인스턴스로
        // 카운터만 되돌린다 — 대기 큐는 스냅샷 범위 밖이므로 새 인스턴스로 자연히 비운다.
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
        const int specCount = 20;
        for (var i = 0; i < specCount; i++)
        {
            var spec = EffectTestKit.MinimalDuration($"buff{i}", ticks: 5 + i % 15);
            var attributeId = i % 2 == 0 ? EffectTestKit.Attack : EffectTestKit.Hp;
            spec.Modifiers.Add(new ModifierDef
            {
                AttributeId = attributeId,
                Op = i % 3 == 0 ? AttributeModifierOp.Multiply : AttributeModifierOp.Add,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(i % 5 + 1) },
            });
            kit.AddSpec(spec);
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
                var specId = kit.SpecId($"buff{rng.NextUInt32() % specCount}");
                pipeline.EnqueueApply(specId, kit.Attacker.Id, target.Id);
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

    private static void RemoveAllActive(EffectPipeline pipeline, EffectTarget target)
    {
        while (target.ActiveEffectCount > 0)
            pipeline.RemoveById(target.Id, target.ActiveEffects[0].Id);
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
        kit.AddSpec(buff);

        var poison = EffectTestKit.MinimalDuration("poison", ticks: 2000);
        poison.PeriodTicks = 1;
        poison.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-1) },
        });
        kit.AddSpec(poison);

        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 50);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 50);

        pipeline.EnqueueApply(kit.SpecId("buff"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.EnqueueApply(kit.SpecId("poison"), kit.Attacker.Id, kit.Defender.Id);

        // 워밍업 — Hp가 0으로 완전히 바닥나 더 이상 Current가 바뀌지 않는 정착 상태까지 튁을 태운다.
        // 그 과정에서 이벤트/변경 버퍼가 필요한 최대 용량까지 미리 자라난다.
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
        Assert.That(allocated, Is.Zero, "정착 상태 Tick() 루프에서 힙 할당 발생");
    }
}
