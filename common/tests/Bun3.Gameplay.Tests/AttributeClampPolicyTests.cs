#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AttributeClampPolicyTests
{
    private const ushort Hp = 1;
    private const ushort MaxHp = 2;

    private static AttributeSet CreateSet(MaxIncreasePolicy increase, MaxDecreasePolicy decrease)
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(MaxHp, min: Operand.Constant(1));
        builder.Register(Hp,
            min: Operand.Constant(0),
            max: Operand.Attribute(MaxHp),
            onMaxIncrease: increase,
            onMaxDecrease: decrease);
        Span<ushort> ids = stackalloc ushort[] { Hp, MaxHp };
        var set = new AttributeSet(builder.Build(), ids);
        set.SetBase(MaxHp, 1000);
        return set;
    }

    [Test]
    public void Decrease_follow_truncates_base_permanently()
    {
        var set = CreateSet(MaxIncreasePolicy.Stay, MaxDecreasePolicy.Follow);
        set.SetBase(Hp, 800);
        set.SetBase(MaxHp, 500);                       // curse
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)500));   // immediate propagation — no observation window
        Assert.That(set.GetBase(Hp), Is.EqualTo((BigNum)500));      // base recorded

        set.SetBase(MaxHp, 1000);                      // curse lifted
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)500));   // loss is permanent
    }

    [Test]
    public void Decrease_stay_preserves_base_and_restores_on_bound_return()
    {
        var set = CreateSet(MaxIncreasePolicy.Stay, MaxDecreasePolicy.Stay);
        set.SetBase(Hp, 800);
        set.SetBase(MaxHp, 500);
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)500));   // safety net
        Assert.That(set.GetBase(Hp), Is.EqualTo((BigNum)800));      // preserved

        set.SetBase(MaxHp, 1000);
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)800));   // restored
    }

    [Test]
    public void Increase_follow_carries_delta_and_buff_cycling_heals()
    {
        var set = CreateSet(MaxIncreasePolicy.Follow, MaxDecreasePolicy.Follow);
        set.SetBase(Hp, 600);

        set.SetBase(MaxHp, 1500);                      // +500 buff
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)1100)); // delta moves with max

        set.SetBase(MaxHp, 1000);                      // buff expires
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)1000)); // clamped — net +400 (known property)
    }

    [Test]
    public void Increase_stay_leaves_base_untouched()
    {
        var set = CreateSet(MaxIncreasePolicy.Stay, MaxDecreasePolicy.Follow);
        set.SetBase(Hp, 600);
        set.SetBase(MaxHp, 2000);
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)600));
    }
}
