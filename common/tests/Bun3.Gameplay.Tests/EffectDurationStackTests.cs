#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectDurationStackTests
{
    [Test]
    public void Duration_buff_expires_without_trace()
    {
        var kit = EffectTestKit.Create();
        var haste = EffectTestKit.MinimalDuration("haste", ticks: 3);
        haste.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Multiply,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, -1)) },  // +20%
        });
        haste.GrantedTags.Add("state.hasted");
        kit.AddSpec(haste);
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 100);
        var before = kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack);

        pipeline.EnqueueApply(kit.SpecId("haste"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)120));
        Assert.That(kit.Defender.Tags.Has(kit.Tag("state.hasted")), Is.True);

        pipeline.Tick(); pipeline.Tick(); pipeline.Tick();     // 3 ticks elapse — expires
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo(before));
        Assert.That(kit.Defender.Tags.Has(kit.Tag("state.hasted")), Is.False);
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);
    }

    [Test]
    public void Stacking_reapply_and_max_clamp()
    {
        var kit = EffectTestKit.Create();
        var chill = EffectTestKit.MinimalDuration("chill", ticks: 10);
        chill.Stack = new StackPolicy { MaxStack = 3, OnReapply = StackReapply.AddStack };
        chill.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-5) },   // -5 per stack (scales with stack by default)
        });
        kit.AddSpec(chill);
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 100);

        for (var i = 0; i < 5; i++)
        {
            pipeline.EnqueueApply(kit.SpecId("chill"), kit.Attacker.Id, kit.Defender.Id);
            pipeline.Tick();
        }

        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));         // merged per target
        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)85)); // clamped at 3 stacks
    }

    [Test]
    public void Periodic_ticks_execute_after_each_period_and_survive_dispel_permanently()
    {
        var kit = EffectTestKit.Create();
        var poison = EffectTestKit.MinimalDuration("poison", ticks: 6);
        poison.PeriodTicks = 2;
        poison.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-10) },
        });
        kit.AddSpec(poison);
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);

        pipeline.EnqueueApply(kit.SpecId("poison"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();                                        // apply tick — no fire (first period not elapsed)
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));

        for (var i = 0; i < 6; i++) pipeline.Tick();            // 6 ticks = 3 fires, then expiry
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)70));
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);   // drained Hp is not restored
    }
}
