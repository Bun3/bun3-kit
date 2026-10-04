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
    public void A_standalone_pipeline_clears_its_buffers_when_the_next_tick_starts()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInfinite("aura"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("aura"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(pipeline.PendingEffectEvents.Length, Is.EqualTo(1));
        Assert.That(pipeline.PendingEffectEvents[0].Target, Is.EqualTo(kit.Defender.Id));

        pipeline.Tick();
        Assert.That(pipeline.PendingEffectEvents.Length, Is.Zero);
    }

    [Test]
    public void A_standalone_pipeline_records_the_attribute_changes_it_makes()
    {
        var kit = EffectTestKit.Create();
        var bolt = EffectTestKit.MinimalInstant("bolt");
        bolt.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-30) },
        });
        kit.AddEffect(bolt);
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);

        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        var changes = pipeline.PendingAttributeChanges;
        Assert.That(changes.Length, Is.EqualTo(1));
        Assert.That(changes[0].Target, Is.EqualTo(kit.Defender.Id));
        Assert.That(changes[0].AttributeId, Is.EqualTo(EffectTestKit.Hp));
        Assert.That(changes[0].NewCurrent, Is.EqualTo((BigNum)70));
    }

    [Test]
    public void A_removed_instance_is_not_recycled_into_another_pipeline()
    {
        var first = EffectTestKit.Create();
        first.AddEffect(EffectTestKit.MinimalDuration("buff", ticks: 1));
        var firstPipeline = first.BuildPipeline();
        firstPipeline.EnqueueApply(first.EffectId("buff"), first.Defender.Id, first.Defender.Id);
        firstPipeline.Tick();
        var removed = first.Defender.ActiveEffects[0];
        for (var i = 0; i < 3; i++) firstPipeline.Tick();
        Assert.That(first.Defender.ActiveEffectCount, Is.Zero);

        var second = EffectTestKit.Create();
        second.AddEffect(EffectTestKit.MinimalDuration("buff", ticks: 1));
        var secondPipeline = second.BuildPipeline();
        secondPipeline.EnqueueApply(second.EffectId("buff"), second.Defender.Id, second.Defender.Id);
        secondPipeline.Tick();

        Assert.That(second.Defender.ActiveEffects[0], Is.Not.SameAs(removed));
    }

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
        kit.AddEffect(backlash);
        var reward = EffectTestKit.MinimalInstant("reward");
        reward.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(+40) },
        });
        kit.AddEffect(reward);

        var curse = EffectTestKit.MinimalDuration("curse", ticks: 100);
        curse.AssetTags.Add("effect.magic.curse");
        curse.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnCompletePrematurely, "backlash"));
        curse.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnCompleteNormal, "reward"));
        kit.AddEffect(curse);
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);

        pipeline.EnqueueApply(kit.EffectId("curse"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        var dispel = kit.TagCatalog.CreateContainer();
        dispel.Add(kit.Tag("effect.magic"));                    // hierarchical match — includes curse
        Assert.That(pipeline.RemoveByTags(kit.Defender.Id, dispel), Is.EqualTo(1));
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);

        pipeline.Tick();                                        // process chains
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)60)); // backlash only
    }

    [Test]
    public void RemoveById_removes_matching_instance()
    {
        var kit = EffectTestKit.Create();
        var buff = EffectTestKit.MinimalDuration("buff", ticks: 100);
        kit.AddEffect(buff);
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("buff"), kit.Attacker.Id, kit.Defender.Id);
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
        kit.AddEffect(buff);
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("buff"), kit.Attacker.Id, kit.Defender.Id);
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
        kit.AddEffect(curse);
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("curse"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        var query = kit.TagCatalog.CreateContainer();
        query.Add(kit.Tag("effect.frost"));                     // unrelated family — no match
        Assert.That(pipeline.RemoveByTags(kit.Defender.Id, query), Is.Zero);
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
    }
}
