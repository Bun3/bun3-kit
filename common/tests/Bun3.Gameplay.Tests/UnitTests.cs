#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class UnitTests
{
    private static Unit CreateUnit(EffectTestKit kit, ulong id, ulong ownerId = 0)
    {
        ReadOnlySpan<ushort> attributes = stackalloc ushort[]
        {
            EffectTestKit.Hp, EffectTestKit.MaxHp, EffectTestKit.Mp,
            EffectTestKit.Attack, EffectTestKit.Resistance,
        };
        return new Unit(new TargetId(id), EffectTestKit.BuildAttributeRegistry(), attributes, kit.TagCatalog, ownerId);
    }

    [Test]
    public void Forwarding_properties_expose_the_same_instances_as_the_effect_target()
    {
        var kit = EffectTestKit.Create();
        var unit = CreateUnit(kit, 1);

        Assert.That(unit.Attributes, Is.SameAs(unit.Effects.Attributes));
        Assert.That(unit.Tags, Is.SameAs(unit.Effects.Tags));
        Assert.That(unit.Effects.Id, Is.EqualTo(unit.Id));
    }

    [Test]
    public void Owner_defaults_to_zero_meaning_unowned()
    {
        var kit = EffectTestKit.Create();

        Assert.That(CreateUnit(kit, 1).OwnerId, Is.Zero);
        Assert.That(CreateUnit(kit, 2, ownerId: 99).OwnerId, Is.EqualTo(99UL));
    }

    [Test]
    public void Snapshot_round_trip_restores_attributes_abilities_and_owner()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var catalog = kit.Catalog;

        var unit = CreateUnit(kit, 1, ownerId: 7);
        unit.Attributes.SetBase(EffectTestKit.MaxHp, 200);
        unit.Attributes.SetBase(EffectTestKit.Hp, 150);
        unit.Abilities.Grant(abilityId: 3, GrantSource.Archetype(), level: 2);

        var snapshot = unit.CreateSnapshot();

        unit.Attributes.SetBase(EffectTestKit.Hp, 10);
        unit.Abilities.Revoke(GrantSource.Archetype());
        unit.OwnerId = 0;

        unit.RestoreSnapshot(snapshot, catalog);

        Assert.That(unit.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)150));
        Assert.That(unit.Abilities.GetLevel(3), Is.EqualTo(2));
        Assert.That(unit.OwnerId, Is.EqualTo(7UL));
    }

    [Test]
    public void Restoring_a_snapshot_from_another_unit_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var catalog = kit.Catalog;

        var snapshot = CreateUnit(kit, 1).CreateSnapshot();
        var other = CreateUnit(kit, 2);

        Assert.That(() => other.RestoreSnapshot(snapshot, catalog), Throws.ArgumentException);
    }
}
