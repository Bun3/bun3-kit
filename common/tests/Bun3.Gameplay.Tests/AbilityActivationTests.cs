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

    private static AbilityActivationResult Activate(Fixture f, TargetId unitId, int abilityId)
    {
        f.World.EnqueueActivation(unitId, abilityId);
        f.World.Tick();
        var results = f.World.PendingActivations;
        return results[results.Length - 1].Result;
    }

    [Test]
    public void Cooldown_gates_on_the_cooldown_effects_granted_tags_without_blocked_tags()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.fireball";
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.Tick();

        Assert.That(f.World.PendingActivations[0].Result, Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.PendingActivations[1].Result, Is.EqualTo(AbilityActivationResult.OnCooldown));
    }

    [Test]
    public void A_shared_cooldown_tag_from_another_ability_blocks_and_is_reported()
    {
        var f = Build((kit, b) =>
        {
            var meteorCooldown = EffectTestKit.MinimalDuration("cooldown.meteor", ticks: 30);
            meteorCooldown.GrantedTags.Add("cooldown.fireball");
            kit.AddEffect(meteorCooldown);

            var fireball = Fireball();
            fireball.CooldownEffect = "cooldown.fireball";
            b.Add(fireball);

            var meteor = new AbilityDef { Name = "meteor", MaxLevel = 1, CooldownEffect = "cooldown.meteor" };
            meteor.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
            b.Add(meteor);
        });
        var fireballId = f.Abilities.GetRequiredId("fireball");
        var meteorId = f.Abilities.GetRequiredId("meteor");
        f.Caster.Abilities.Grant(fireballId, GrantSource.Archetype(), level: 1);
        f.Caster.Abilities.Grant(meteorId, GrantSource.Archetype(), level: 1);

        Assert.That(Activate(f, f.Caster.Id, meteorId), Is.EqualTo(AbilityActivationResult.Success));

        Assert.That(f.World.CanActivateAbility(f.Caster.Id, fireballId), Is.EqualTo(AbilityActivationResult.OnCooldown));
        Assert.That(f.World.TryGetCooldown(f.Caster.Id, fireballId, out var remaining, out var duration), Is.True);
        Assert.That(duration, Is.EqualTo(30));
        Assert.That(remaining, Is.EqualTo(30));
    }

    [Test]
    public void Try_get_cooldown_is_false_when_ready_or_without_a_cooldown_effect()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.fireball";
            b.Add(ability);
            var plain = new AbilityDef { Name = "plain", MaxLevel = 1 };
            plain.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
            b.Add(plain);
        });
        var id = f.Abilities.GetRequiredId("fireball");

        Assert.That(f.World.TryGetCooldown(f.Caster.Id, id, out _, out _), Is.False);
        Assert.That(f.World.TryGetCooldown(f.Caster.Id, f.Abilities.GetRequiredId("plain"), out _, out _), Is.False);
        Assert.That(f.World.TryGetCooldown(new TargetId(999), id, out _, out _), Is.False);
    }

    [Test]
    public void A_command_that_throws_surfaces_once_and_the_world_keeps_ticking()
    {
        var f = Build((kit, b) =>
        {
            var scaled = EffectTestKit.MinimalDuration("cooldown.scaled", ticks: 10);
            scaled.GrantedTags.Add("cooldown.fireball");
            scaled.DurationScale = new MagnitudeDef { Base = Operand.Attribute(EffectTestKit.Resistance) };
            kit.AddEffect(scaled);
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.scaled";
            ability.Costs.Add(new CostDef { AttributeId = EffectTestKit.Mp, Magnitude = new MagnitudeDef { Base = Operand.Constant(10) } });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        ReadOnlySpan<ushort> declared = stackalloc ushort[] { EffectTestKit.Hp, EffectTestKit.MaxHp, EffectTestKit.Mp };
        var bare = new Unit(new TargetId(3), EffectTestKit.BuildAttributeRegistry(), declared, f.Kit.TagCatalog);
        bare.Attributes.SetBase(EffectTestKit.Mp, 50);
        f.World.Spawn(bare);
        bare.Abilities.Grant(id, GrantSource.Archetype(), level: 1);
        f.World.Tick();
        var tickBefore = f.World.CurrentTick;

        // The cooldown's DurationScale reads an attribute this unit does not declare.
        f.World.EnqueueActivation(bare.Id, id);
        Assert.That(() => f.World.Tick(), Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(() => f.World.Tick(), Throws.Nothing);

        Assert.That(f.World.PendingCommandCount, Is.Zero);
        Assert.That(bare.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)40), "the cost is not charged again");
        Assert.That(f.World.CurrentTick, Is.EqualTo(tickBefore + 1));
    }

    [Test]
    public void Grant_and_revoke_commands_keep_call_order_with_activation()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");

        f.World.EnqueueGrant(f.Caster.Id, id, GrantSource.Code(), level: 1);
        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.EnqueueRevoke(f.Caster.Id, GrantSource.Code());
        f.World.EnqueueActivation(f.Caster.Id, id);
        Assert.That(f.Caster.Abilities.IsGranted(id), Is.False, "nothing runs outside the tick");
        f.World.Tick();

        Assert.That(f.World.PendingActivations[0].Result, Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.PendingActivations[1].Result, Is.EqualTo(AbilityActivationResult.NotGranted));
        Assert.That(f.Caster.Abilities.IsGranted(id), Is.False);
    }

    [Test]
    public void Enqueue_grant_rejects_an_unknown_ability_or_a_level_below_one_at_the_call()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");

        Assert.That(
            () => f.World.EnqueueGrant(f.Caster.Id, 999, GrantSource.Code(), level: 1),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => f.World.EnqueueGrant(f.Caster.Id, id, GrantSource.Code(), level: 0),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(f.World.PendingCommandCount, Is.Zero);
    }

    [Test]
    public void Activation_is_queued_and_reported_after_the_tick()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.EnqueueActivation(f.Caster.Id, id);
        Assert.That(f.Victim.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100), "nothing runs outside the tick");
        Assert.That(f.World.PendingCommandCount, Is.EqualTo(1));

        f.World.Tick();

        Assert.That(f.World.PendingActivations.Length, Is.EqualTo(1));
        Assert.That(f.World.PendingActivations[0].UnitId, Is.EqualTo(f.Caster.Id));
        Assert.That(f.World.PendingActivations[0].AbilityId, Is.EqualTo(id));
        Assert.That(f.World.PendingActivations[0].Result, Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.PendingCommandCount, Is.Zero);
    }

    [Test]
    public void Can_activate_reports_the_gate_result_without_side_effects()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.fireball";
            ability.Costs.Add(new CostDef { AttributeId = EffectTestKit.Mp, Magnitude = new MagnitudeDef { Base = Operand.Constant(30) } });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.CanActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.CanActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)50));
        Assert.That(f.Caster.Effects.ActiveEffectCount, Is.Zero);
        Assert.That(f.World.PendingCommandCount, Is.Zero);
        Assert.That(f.World.Pipeline.PendingApplyCount, Is.Zero);
    }

    [Test]
    public void Commands_keep_call_order_across_kinds()
    {
        var f = Build((kit, b) =>
        {
            kit.AddEffect(EffectTestKit.MinimalInfinite("mark.a"));
            kit.AddEffect(EffectTestKit.MinimalInfinite("mark.b"));
            var ability = new AbilityDef { Name = "mark", MaxLevel = 1 };
            ability.Effects.Add(new AbilityEffectDef { EffectName = "mark.a" });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("mark");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.EnqueueApply(f.Kit.EffectId("mark.b"), f.Caster.Id, f.Caster.Id);
        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.Tick();

        var active = f.Caster.Effects.ActiveEffects;
        Assert.That(active[0].EffectId, Is.EqualTo(f.Kit.EffectId("mark.b")), "applied first, so the lower instance id");
        Assert.That(active[1].EffectId, Is.EqualTo(f.Kit.EffectId("mark.a")));
    }

    [Test]
    public void Activation_for_a_unit_despawned_this_frame_reports_unknown_unit()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.Despawn(f.Caster.Id);
        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.UnknownUnit));
    }

    [Test]
    public void A_cost_on_an_attribute_the_unit_does_not_declare_is_insufficient_not_an_exception()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.Costs.Add(new CostDef { AttributeId = EffectTestKit.Resistance, Magnitude = new MagnitudeDef { Base = Operand.Constant(1) } });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        ReadOnlySpan<ushort> declared = stackalloc ushort[] { EffectTestKit.Hp, EffectTestKit.MaxHp };
        var bare = new Unit(new TargetId(3), EffectTestKit.BuildAttributeRegistry(), declared, f.Kit.TagCatalog);
        f.World.Spawn(bare);
        bare.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(Activate(f, bare.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
    }

    [Test]
    public void Unknown_unit_and_ability_are_reported_as_such()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");

        Assert.That(Activate(f, new TargetId(99), id), Is.EqualTo(AbilityActivationResult.UnknownUnit));
        Assert.That(Activate(f, f.Caster.Id, 12345), Is.EqualTo(AbilityActivationResult.UnknownAbility));
    }

    [Test]
    public void Ungranted_ability_cannot_be_activated()
    {
        var f = Build((_, b) => b.Add(Fireball()));

        Assert.That(
            Activate(f, f.Caster.Id, f.Abilities.GetRequiredId("fireball")),
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

        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.MissingTag));

        f.Caster.Tags.Add(f.Kit.Tag("state.armed"));
        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
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

        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.BlockedByTag));
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

        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.ConditionFailed));
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

        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
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

        f.World.EnqueueApply(f.Kit.EffectId("mp.boost"), f.Caster.Id, f.Caster.Id);
        f.World.Tick();
        Assert.That(f.Caster.Attributes.GetCurrent(EffectTestKit.Mp), Is.EqualTo((BigNum)90));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)50));

        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
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

        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
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

        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
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

        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.Tick();

        Assert.That(f.World.PendingActivations[0].Result, Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.PendingActivations[1].Result, Is.EqualTo(AbilityActivationResult.CostInsufficient));
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

        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.Tick();

        Assert.That(f.World.PendingActivations[0].Result, Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.PendingActivations[1].Result, Is.EqualTo(AbilityActivationResult.OnCooldown));
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

        Activate(f, f.Caster.Id, id);
        for (var i = 0; i < 8; i++) f.World.Tick();

        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
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
        f.World.EnqueueApply(f.Kit.EffectId("ring.cdr"), f.Caster.Id, f.Caster.Id);
        f.World.Tick();

        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));

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

        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
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
        world.EnqueueActivation(caster.Id, id);
        world.Tick();

        Assert.That(caster.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)70));
    }
}
