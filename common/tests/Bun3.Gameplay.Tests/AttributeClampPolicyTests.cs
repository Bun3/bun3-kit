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
        set.SetBase(MaxHp, 500);                       // 저주
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)500));   // 즉시 전파 — 관찰 창 없음
        Assert.That(set.GetBase(Hp), Is.EqualTo((BigNum)500));      // Base 기록

        set.SetBase(MaxHp, 1000);                      // 저주 해제
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)500));   // 소실 영구
    }

    [Test]
    public void Decrease_stay_preserves_base_and_restores_on_bound_return()
    {
        var set = CreateSet(MaxIncreasePolicy.Stay, MaxDecreasePolicy.Stay);
        set.SetBase(Hp, 800);
        set.SetBase(MaxHp, 500);
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)500));   // 안전망
        Assert.That(set.GetBase(Hp), Is.EqualTo((BigNum)800));      // 보존

        set.SetBase(MaxHp, 1000);
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)800));   // 복원
    }

    [Test]
    public void Increase_follow_carries_delta_and_buff_cycling_heals()
    {
        var set = CreateSet(MaxIncreasePolicy.Follow, MaxDecreasePolicy.Follow);
        set.SetBase(Hp, 600);

        set.SetBase(MaxHp, 1500);                      // +500 버프
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)1100)); // Δ 동반

        set.SetBase(MaxHp, 1000);                      // 버프 만료
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)1000)); // 잘림 — 순 +400 (알려진 성질)
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
