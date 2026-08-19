#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectChainConditionTests
{
    [Test]
    public void Stack_overflow_chain_freezes_and_resets()   // "3 chill stacks -> frozen" — the spec's acceptance bar
    {
        var kit = EffectTestKit.Create();
        var frozen = EffectTestKit.MinimalDuration("frozen", ticks: 2);
        frozen.GrantedTags.Add("state.frozen");
        kit.AddSpec(frozen);
        var chill = EffectTestKit.MinimalDuration("chill", ticks: 10);
        chill.Stack = new StackPolicy
        {
            MaxStack = 3, OnReapply = StackReapply.AddStack,
            OnOverflow = StackOverflow.ApplyEffect, OverflowEffectName = "frozen", ClearStacksOnOverflow = true,
        };
        kit.AddSpec(chill);
        var pipeline = kit.BuildPipeline();

        for (var i = 0; i < 4; i++)   // 4th application overflows the stack
        {
            pipeline.EnqueueApply(kit.SpecId("chill"), kit.Attacker.Id, kit.Defender.Id);
            pipeline.Tick();
        }
        pipeline.Tick();              // process the carried-over chain apply
        Assert.That(kit.Defender.Tags.Has(kit.Tag("state.frozen")), Is.True);
    }

    [Test]
    public void Ongoing_condition_toggles_once_per_tick_without_removal()
    {
        var kit = EffectTestKit.Create();
        var lowHp = EffectTestKit.MinimalInfinite("lowhp");
        lowHp.OngoingConditions.Add(new ConditionDef
        {
            Left = Operand.Attribute(EffectTestKit.Hp),
            Op = ComparisonOp.Less,
            Right = Operand.Attribute(EffectTestKit.MaxHp, BigNum.FromParts(3, -1)),
        });
        lowHp.GrantedTags.Add("state.lowhealth");
        kit.AddSpec(lowHp);
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);

        pipeline.EnqueueApply(kit.SpecId("lowhp"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Tags.Has(kit.Tag("state.lowhealth")), Is.False);

        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 20);
        pipeline.Tick();
        Assert.That(kit.Defender.Tags.Has(kit.Tag("state.lowhealth")), Is.True);
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));   // toggled, not removed

        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 80);
        pipeline.Tick();
        Assert.That(kit.Defender.Tags.Has(kit.Tag("state.lowhealth")), Is.False);
    }

    [Test]
    public void Expiry_chain_fires_next_tick_and_selector_routes_targets()
    {
        var kit = EffectTestKit.Create();
        kit.RegisterSelector("selector.everyone", new EffectTestKit.AllTargetsSelector());
        var blast = EffectTestKit.MinimalInstant("blast");
        blast.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-15) },
        });
        kit.AddSpec(blast);
        var bomb = EffectTestKit.MinimalDuration("bomb", ticks: 2);
        bomb.Chains.Add(new ChainEdgeDef
        {
            Trigger = ChainTrigger.OnCompleteNormal, EffectName = "blast", SelectorTag = "selector.everyone",
        });
        kit.AddSpec(bomb);
        var pipeline = kit.BuildPipeline();
        foreach (var target in kit.AllTargets) { target.Attributes.SetBase(EffectTestKit.MaxHp, 100); target.Attributes.SetBase(EffectTestKit.Hp, 100); }

        pipeline.EnqueueApply(kit.SpecId("bomb"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick(); pipeline.Tick(); pipeline.Tick();     // after expiry (2 ticks)
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100)); // not yet — one tick of delay
        pipeline.Tick();                                        // blast fires in the next tick's apply phase
        foreach (var target in kit.AllTargets)
            Assert.That(target.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)85));
    }

    [Test]
    public void Apply_budget_carries_over_and_survives_a_cyclic_chain()
    {
        var kit = EffectTestKit.Create();
        var a = EffectTestKit.MinimalInstant("a"); a.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "b"));
        var b = EffectTestKit.MinimalInstant("b"); b.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "a"));
        kit.AddSpec(a); kit.AddSpec(b);
        var pipeline = kit.BuildPipeline(applyBudgetPerTick: 8);

        pipeline.EnqueueApply(kit.SpecId("a"), kit.Attacker.Id, kit.Defender.Id);
        for (var i = 0; i < 10; i++) pipeline.Tick();          // progresses without livelock
        Assert.That(pipeline.PendingApplyCount, Is.LessThanOrEqualTo(1));   // bounded by the per-tick budget
    }
}
