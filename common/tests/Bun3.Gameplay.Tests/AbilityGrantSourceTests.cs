#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilityGrantSourceTests
{
    private static Unit MakeUnit(EffectTestKit kit, ulong id)
    {
        ReadOnlySpan<ushort> attributes = stackalloc ushort[]
        {
            EffectTestKit.Hp, EffectTestKit.MaxHp, EffectTestKit.Mp,
            EffectTestKit.Attack, EffectTestKit.Resistance,
        };
        return new Unit(new TargetId(id), EffectTestKit.BuildAttributeRegistry(), attributes, kit.TagCatalog);
    }

    private static (World World, Unit Unit, int AbilityId, EffectTestKit Kit) Build(
        Action<EffectSpec> configureGranter)
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("bolt"));

        var granter = EffectTestKit.MinimalInfinite("granter");
        granter.GrantedAbilities.Add("fireball");
        configureGranter(granter);
        kit.AddSpec(granter);

        var ability = new AbilityDef { Name = "fireball", MaxLevel = 1 };
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(ability);
        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        var world = new World(kit.Catalog, abilities, new XorShiftRng(5));
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();
        unit.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        unit.Attributes.SetBase(EffectTestKit.Hp, 100);
        unit.Attributes.SetBase(EffectTestKit.Mp, 50);

        return (world, unit, abilities.GetRequiredId("fireball"), kit);
    }

    [Test]
    public void Applying_the_granting_effect_grants_the_ability()
    {
        var (world, unit, abilityId, kit) = Build(_ => { });

        Assert.That(unit.Abilities.IsGranted(abilityId), Is.False);
        world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
        world.Tick();

        Assert.That(unit.Abilities.IsGranted(abilityId), Is.True);
    }

    [Test]
    public void Removing_the_granting_effect_reclaims_the_ability()
    {
        var (world, unit, abilityId, kit) = Build(_ => { });
        world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
        world.Tick();

        Assert.That(unit.Effects.ActiveEffects.Count, Is.EqualTo(1));
        world.Pipeline.RemoveById(unit.Id, unit.Effects.ActiveEffects[0].Id);
        world.Tick();

        Assert.That(unit.Abilities.IsGranted(abilityId), Is.False);
    }

    [Test]
    public void Disabling_the_granting_effect_toggles_the_grant_off_and_on()
    {
        var (world, unit, abilityId, kit) = Build(spec =>
            spec.OngoingConditions.Add(new ConditionDef
            {
                Left = Operand.Attribute(EffectTestKit.Mp),
                Op = ComparisonOp.Greater,
                Right = Operand.Constant(0),
            }));

        world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
        world.Tick();
        Assert.That(unit.Abilities.IsGranted(abilityId), Is.True);

        unit.Attributes.SetBase(EffectTestKit.Mp, 0);
        world.Tick();
        Assert.That(unit.Abilities.IsGranted(abilityId), Is.False);

        unit.Attributes.SetBase(EffectTestKit.Mp, 10);
        world.Tick();
        Assert.That(unit.Abilities.IsGranted(abilityId), Is.True);
    }

    [Test]
    public void Unknown_granted_ability_name_fails_when_the_world_is_created()
    {
        var kit = EffectTestKit.Create();
        var granter = EffectTestKit.MinimalInfinite("granter");
        granter.GrantedAbilities.Add("nope");
        kit.AddSpec(granter);
        var abilities = new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        Assert.That(
            () => new World(kit.Catalog, abilities, new XorShiftRng(1)),
            Throws.InvalidOperationException.With.Message.Contains("nope"));
    }

    // ---- LevelFromStack: the granted ability's level must track instance.Level, not just its creation-time value ----

    private static (World World, Unit Unit, int AbilityId, EffectTestKit Kit) BuildLevelFromStack(
        StackPolicy stackPolicy, int durationTicks)
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("bolt"));

        var granter = EffectTestKit.MinimalDuration("granter", durationTicks);
        granter.MaxLevel = 5;
        granter.Stack = stackPolicy;
        granter.GrantedAbilities.Add("fireball");
        kit.AddSpec(granter);

        var ability = new AbilityDef { Name = "fireball", MaxLevel = 5 };
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(ability);
        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        var world = new World(kit.Catalog, abilities, new XorShiftRng(7));
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();

        return (world, unit, abilities.GetRequiredId("fireball"), kit);
    }

    [Test]
    public void LevelFromStack_reapply_raises_the_granted_ability_level()
    {
        var (world, unit, abilityId, kit) = BuildLevelFromStack(
            new StackPolicy { MaxStack = 5, OnReapply = StackReapply.AddStack, LevelFromStack = true },
            durationTicks: 100);

        world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
        world.Tick();
        Assert.That(unit.Abilities.GetLevel(abilityId), Is.EqualTo(1));

        world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
        world.Tick();
        Assert.That(unit.Abilities.GetLevel(abilityId), Is.EqualTo(2));
    }

    [Test]
    public void RemoveOneAndRefresh_stack_drop_lowers_the_granted_ability_level()
    {
        var (world, unit, abilityId, kit) = BuildLevelFromStack(
            new StackPolicy
            {
                MaxStack = 5, OnReapply = StackReapply.AddStack, LevelFromStack = true,
                OnExpiration = StackExpiration.RemoveOneAndRefresh,
            },
            durationTicks: 2);

        world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
        world.Tick();
        world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
        world.Tick();
        Assert.That(unit.Abilities.GetLevel(abilityId), Is.EqualTo(2));

        // No further reapply — the duration runs out and RemoveOneAndRefresh drops exactly one stack.
        world.Tick();
        Assert.That(unit.Abilities.GetLevel(abilityId), Is.EqualTo(1));
    }

    [Test]
    public void LevelFromStack_reapply_does_not_create_duplicate_grant_rows()
    {
        var (world, unit, abilityId, kit) = BuildLevelFromStack(
            new StackPolicy { MaxStack = 5, OnReapply = StackReapply.AddStack, LevelFromStack = true },
            durationTicks: 100);

        for (var i = 0; i < 4; i++)
        {
            world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
            world.Tick();
        }

        Assert.That(unit.Abilities.GetLevel(abilityId), Is.EqualTo(4));
        Assert.That(unit.Abilities.GrantCount, Is.EqualTo(1));
    }
}
