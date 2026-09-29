#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilityActivationTests
{
    private sealed class Fixture
    {
        internal EffectTestKit Kit = null!;
        internal World World = null!;
        internal Unit Caster = null!;
        internal Unit Victim = null!;
        internal AbilityCatalog Abilities = null!;
    }

    private static Unit MakeUnit(EffectTestKit kit, ulong id)
    {
        ReadOnlySpan<ushort> attributes = stackalloc ushort[]
        {
            EffectTestKit.Hp, EffectTestKit.MaxHp, EffectTestKit.Mp,
            EffectTestKit.Attack, EffectTestKit.Resistance,
        };
        return new Unit(new TargetId(id), EffectTestKit.BuildAttributeRegistry(), attributes, kit.TagCatalog);
    }

    private static Fixture Build(Action<EffectTestKit, AbilityCatalogBuilder> configure)
    {
        var kit = EffectTestKit.Create();
        var bolt = EffectTestKit.MinimalInstant("bolt");
        bolt.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-25) },
        });
        kit.AddEffect(bolt);

        var cooldown = EffectTestKit.MinimalDuration("cooldown.fireball", ticks: 5);
        cooldown.GrantedTags.Add("cooldown.fireball");
        kit.AddEffect(cooldown);

        var abilityBuilder = new AbilityCatalogBuilder();
        configure(kit, abilityBuilder);

        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        var world = new World(kit.Catalog, abilities, new XorShiftRng(999));
        var caster = MakeUnit(kit, 1);
        var victim = MakeUnit(kit, 2);
        world.Spawn(caster);
        world.Spawn(victim);
        world.Tick();

        foreach (var unit in new[] { caster, victim })
        {
            unit.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            unit.Attributes.SetBase(EffectTestKit.Hp, 100);
            unit.Attributes.SetBase(EffectTestKit.Mp, 50);
        }

        return new Fixture { Kit = kit, World = world, Caster = caster, Victim = victim, Abilities = abilities };
    }

    private static AbilityDef Fireball()
    {
        var ability = new AbilityDef { Name = "fireball", MaxLevel = 3 };
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt", LevelRule = ChainLevelRule.Inherit });
        return ability;
    }

    [Test]
    public void Unknown_unit_and_ability_are_reported_as_such()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");

        Assert.That(f.World.TryActivateAbility(new TargetId(99), id), Is.EqualTo(AbilityActivationResult.UnknownUnit));
        Assert.That(f.World.TryActivateAbility(f.Caster.Id, 12345), Is.EqualTo(AbilityActivationResult.UnknownAbility));
    }

    [Test]
    public void Ungranted_ability_cannot_be_activated()
    {
        var f = Build((_, b) => b.Add(Fireball()));

        Assert.That(
            f.World.TryActivateAbility(f.Caster.Id, f.Abilities.GetRequiredId("fireball")),
            Is.EqualTo(AbilityActivationResult.NotGranted));
    }

    [Test]
    public void Missing_required_tag_blocks_activation()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.RequiredTags.Add("state.armed");
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.MissingTag));

        f.Caster.Tags.Add(f.Kit.Tag("state.armed"));
        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
    }

    [Test]
    public void Blocked_tag_blocks_activation()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.BlockedTags.Add("state.silenced");
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);
        f.Caster.Tags.Add(f.Kit.Tag("state.silenced"));

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.BlockedByTag));
    }

    [Test]
    public void Failing_activation_condition_blocks_activation()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.ActivationConditions.Add(new ConditionDef
            {
                Left = Operand.Attribute(EffectTestKit.Hp),
                Op = ComparisonOp.Greater,
                Right = Operand.Constant(500),
            });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.ConditionFailed));
    }

    [Test]
    public void Insufficient_cost_blocks_activation_and_spends_nothing()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.Costs.Add(new CostDef
            {
                AttributeId = EffectTestKit.Mp,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(80) },
            });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)50));
    }

    [Test]
    public void A_modifier_that_raises_current_does_not_fund_a_cost_the_base_cannot_afford()
    {
        var f = Build((kit, b) =>
        {
            var boost = EffectTestKit.MinimalInfinite("mp.boost");
            boost.Modifiers.Add(new ModifierDef
            {
                AttributeId = EffectTestKit.Mp,
                Op = AttributeModifierOp.Add,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(40) },
            });
            kit.AddEffect(boost);

            var ability = Fireball();
            ability.Costs.Add(new CostDef
            {
                AttributeId = EffectTestKit.Mp,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(80) },
            });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.Pipeline.EnqueueApply(f.Kit.EffectId("mp.boost"), f.Caster.Id, f.Caster.Id);
        f.World.Tick();
        Assert.That(f.Caster.Attributes.GetCurrent(EffectTestKit.Mp), Is.EqualTo((BigNum)90));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)50));

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)50));
    }

    [Test]
    public void Cost_on_a_min_bounded_attribute_charges_exactly()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.Costs.Add(new CostDef
            {
                AttributeId = EffectTestKit.Attack,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(10) },
            });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);
        f.Caster.Attributes.SetBase(EffectTestKit.Attack, 10);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Attack), Is.EqualTo((BigNum)0));
    }

    [Test]
    public void An_unaffordable_second_cost_leaves_the_first_cost_undebited()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.Costs.Add(new CostDef
            {
                AttributeId = EffectTestKit.Mp,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(10) },
            });
            ability.Costs.Add(new CostDef
            {
                AttributeId = EffectTestKit.Attack,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(999) },
            });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)50));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Attack), Is.EqualTo((BigNum)0));
    }

    [Test]
    public void Cost_is_deducted_immediately_so_repeat_activation_in_one_tick_fails()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.Costs.Add(new CostDef
            {
                AttributeId = EffectTestKit.Mp,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(30) },
            });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)20));
        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)20));
    }

    [Test]
    public void Cooldown_effect_blocks_the_next_activation_in_the_same_tick()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.fireball";
            ability.BlockedTags.Add("cooldown.fireball");
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.BlockedByTag));
    }

    [Test]
    public void Cooldown_expires_and_the_ability_becomes_available_again()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.fireball";
            ability.BlockedTags.Add("cooldown.fireball");
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.TryActivateAbility(f.Caster.Id, id);
        for (var i = 0; i < 8; i++) f.World.Tick();

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
    }

    [Test]
    public void A_twenty_percent_cooldown_reduction_ring_is_pure_data()
    {
        // Stand-in for a game's cooldown-scale attribute: a multiplier whose Base is 1.
        const ushort cooldownScale = EffectTestKit.Resistance;
        var f = Build((kit, b) =>
        {
            var cooldown = EffectTestKit.MinimalDuration("cooldown.scaled", ticks: 100);
            cooldown.GrantedTags.Add("cooldown.fireball");
            cooldown.DurationScale = new MagnitudeDef { Base = Operand.Attribute(cooldownScale) };
            kit.AddEffect(cooldown);

            var ring = EffectTestKit.MinimalInfinite("ring.cdr");
            ring.Modifiers.Add(new ModifierDef
            {
                AttributeId = cooldownScale,
                Op = AttributeModifierOp.Multiply,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(-2, -1)) },   // -20%
            });
            kit.AddEffect(ring);

            var ability = Fireball();
            ability.CooldownEffect = "cooldown.scaled";
            ability.BlockedTags.Add("cooldown.fireball");
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);
        f.Caster.Attributes.SetBase(cooldownScale, 1);
        f.World.Pipeline.EnqueueApply(f.Kit.EffectId("ring.cdr"), f.Caster.Id, f.Caster.Id);
        f.World.Tick();

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));

        var cooldownId = f.Kit.EffectId("cooldown.scaled");
        var active = f.Caster.Effects.ActiveEffects;
        EffectInstance? instance = null;
        for (var i = 0; i < active.Count; i++)
        {
            if (active[i].EffectId == cooldownId) instance = active[i];
        }

        Assert.That(instance, Is.Not.Null);
        Assert.That(instance!.RemainingTicks, Is.EqualTo(80));
    }

    [Test]
    public void Activation_enqueues_the_ability_effects_on_the_caster_by_default()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        f.World.Tick();

        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)75));
        Assert.That(f.Victim.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));
    }

    [Test]
    public void Granted_level_drives_the_applied_effect_level()
    {
        var kit = EffectTestKit.Create();
        var bolt = EffectTestKit.MinimalInstant("bolt");
        bolt.MaxLevel = 3;
        bolt.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef
            {
                PerLevelValues = new System.Collections.Generic.List<BigNum> { -10, -20, -30 },
            },
        });
        kit.AddEffect(bolt);

        var ability = new AbilityDef { Name = "fireball", MaxLevel = 3 };
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt", LevelRule = ChainLevelRule.Inherit });
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(ability);
        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        var world = new World(kit.Catalog, abilities, new XorShiftRng(7));
        var caster = MakeUnit(kit, 1);
        world.Spawn(caster);
        world.Tick();
        caster.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        caster.Attributes.SetBase(EffectTestKit.Hp, 100);

        var id = abilities.GetRequiredId("fireball");
        caster.Abilities.Grant(id, GrantSource.Archetype(), level: 3);
        world.TryActivateAbility(caster.Id, id);
        world.Tick();

        Assert.That(caster.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)70));
    }
}
