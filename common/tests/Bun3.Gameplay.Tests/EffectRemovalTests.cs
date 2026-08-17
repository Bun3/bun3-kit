#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectRemovalTests
{
    [Test]
    public void Dispel_fires_premature_chain_but_not_normal()
    {
        var kit = EffectTestKit.Create();
        var backlash = EffectTestKit.MinimalInstant("backlash");
        backlash.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-40) },
        });
        kit.AddSpec(backlash);
        var reward = EffectTestKit.MinimalInstant("reward");
        reward.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(+40) },
        });
        kit.AddSpec(reward);

        var curse = EffectTestKit.MinimalDuration("curse", ticks: 100);
        curse.AssetTags.Add("effect.magic.curse");
        curse.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnCompletePrematurely, "backlash"));
        curse.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnCompleteNormal, "reward"));
        kit.AddSpec(curse);
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);

        pipeline.EnqueueApply(kit.SpecId("curse"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        var dispel = kit.TagCatalog.CreateContainer();
        dispel.Add(kit.Tag("effect.magic"));                    // 계층 매칭 — curse 포함
        Assert.That(pipeline.RemoveByTags(kit.Defender.Id, dispel), Is.EqualTo(1));
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);

        pipeline.Tick();                                        // 체인 처리
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)60)); // backlash만
    }

    [Test]
    public void RemoveById_removes_matching_instance()
    {
        var kit = EffectTestKit.Create();
        var buff = EffectTestKit.MinimalDuration("buff", ticks: 100);
        kit.AddSpec(buff);
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("buff"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));

        var instanceId = kit.Defender.ActiveEffects[0].Id;
        Assert.That(pipeline.RemoveById(kit.Defender.Id, instanceId), Is.True);
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);
    }

    [Test]
    public void RemoveById_returns_false_for_unknown_id()
    {
        var kit = EffectTestKit.Create();
        var buff = EffectTestKit.MinimalDuration("buff", ticks: 100);
        kit.AddSpec(buff);
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("buff"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(pipeline.RemoveById(kit.Defender.Id, instanceId: 9999), Is.False);
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
    }

    [Test]
    public void RemoveByTags_returns_zero_when_query_does_not_match()
    {
        var kit = EffectTestKit.Create();
        var curse = EffectTestKit.MinimalDuration("curse", ticks: 100);
        curse.AssetTags.Add("effect.magic.curse");
        kit.AddSpec(curse);
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("curse"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        var query = kit.TagCatalog.CreateContainer();
        query.Add(kit.Tag("effect.frost"));                     // curse와 무관한 계열 — 매칭 없음
        Assert.That(pipeline.RemoveByTags(kit.Defender.Id, query), Is.Zero);
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
    }
}
