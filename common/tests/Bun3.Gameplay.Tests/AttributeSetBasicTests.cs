#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AttributeSetBasicTests
{
    [Test]
    public void A_clamp_bound_missing_from_the_declaration_is_named_in_the_error()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(10, "Health", min: Operand.Constant(0), max: Operand.Attribute(11));
        builder.Register(11, "MaxHealth");
        var registry = builder.Build();

        var declared = new ushort[] { 10 };
        var ex = Assert.Throws<ArgumentException>(() => new AttributeSet(registry, declared));
        Assert.That(ex!.Message, Does.Contain("'Health'").And.Contain("'MaxHealth'"));
    }

    private const ushort Hp = 1;
    private const ushort MaxHp = 2;
    private const ushort Attack = 5;

    private static AttributeRegistry BuildRegistry()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(MaxHp, "MaxHp", min: Operand.Constant(1));
        builder.Register(Hp, "Hp", min: Operand.Constant(0), max: Operand.Attribute(MaxHp));
        builder.Register(Attack, "Attack");
        return builder.Build();
    }

    private static AttributeSet CreateSet()
    {
        var registry = BuildRegistry();
        Span<ushort> ids = stackalloc ushort[] { Attack, Hp, MaxHp };   // any order — canonicalized internally
        return new AttributeSet(registry, ids);
    }

    [Test]
    public void Base_writes_always_pass_through_clamp()
    {
        var set = CreateSet();
        set.SetBase(MaxHp, 1000);
        set.SetBase(Hp, 800);

        set.AddBase(Hp, 500);                       // overheal
        Assert.That(set.GetBase(Hp), Is.EqualTo((BigNum)1000));

        set.AddBase(Hp, -1300);                     // lethal damage
        Assert.That(set.GetBase(Hp), Is.EqualTo(BigNum.Zero));

        set.SetBase(Attack, -50);                   // unclamped attributes are unrestricted
        Assert.That(set.GetCurrent(Attack), Is.EqualTo((BigNum)(-50)));
    }

    [Test]
    public void Change_events_carry_old_and_new_and_skip_no_ops()
    {
        var set = CreateSet();
        set.SetBase(MaxHp, 1000);                   // not attached yet — not recorded
        var log = new AttributeChangeLog();
        set.AttachChangeLog(log, new TargetId(7));

        set.SetBase(Hp, 800);
        set.SetBase(Hp, 800);                       // same value — no event
        Assert.That(log.Pending.Length, Is.EqualTo(1));
        Assert.That(log.Pending[0].Target, Is.EqualTo(new TargetId(7)));
        Assert.That(log.Pending[0].AttributeId, Is.EqualTo(Hp));
        Assert.That(log.Pending[0].OldCurrent, Is.EqualTo(BigNum.Zero));
        Assert.That(log.Pending[0].NewCurrent, Is.EqualTo((BigNum)800));

        set.AttachChangeLog(null, default);
        set.SetBase(Hp, 700);
        Assert.That(log.Pending.Length, Is.EqualTo(1), "detached sets record nothing");
    }

    [Test]
    public void Unknown_attribute_access_throws_and_has_reports_membership()
    {
        var set = CreateSet();
        Assert.That(set.Has(Hp), Is.True);
        Assert.That(set.Has(999), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => set.GetCurrent(999));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.SetBase(999, 1));
    }

    [Test]
    public void Clamp_bound_reference_must_be_declared()
    {
        // registry: Hp(max→MaxHp), MaxHp(no clamp)
        var builder = new AttributeRegistryBuilder();
        builder.Register(MaxHp, "MaxHp");
        builder.Register(Hp, "Hp", min: Operand.Constant(0), max: Operand.Attribute(MaxHp));
        var registry = builder.Build();

        // creation fails when the set omits MaxHp
        var ids = new ushort[] { Hp };
        Assert.Throws<ArgumentException>(() => new AttributeSet(registry, ids));
    }
}
