#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilityCatalogBuilderTests
{
    private static AbilityDef Minimal(string name) => new AbilityDef { Name = name };

    // Effect with a real leveled magnitude — MaxLevel 5, one PerLevelValues entry per level.
    private static EffectSpec LeveledBolt(string name)
    {
        var spec = EffectTestKit.MinimalInstant(name);
        spec.MaxLevel = 5;
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack,
            Magnitude = new MagnitudeDef { PerLevelValues = new List<BigNum> { 1, 2, 3, 4, 5 } },
        });
        return spec;
    }

    [Test]
    public void Build_assigns_dense_ids_in_insertion_order()
    {
        var kit = EffectTestKit.Create();
        var builder = new AbilityCatalogBuilder();
        builder.Add(Minimal("alpha"));
        builder.Add(Minimal("beta"));

        var catalog = builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.GetRequiredId("alpha"), Is.EqualTo(0));
        Assert.That(catalog.GetRequiredId("beta"), Is.EqualTo(1));
    }

    [Test]
    public void Duplicate_ability_names_are_rejected()
    {
        var kit = EffectTestKit.Create();
        var builder = new AbilityCatalogBuilder();
        builder.Add(Minimal("dup"));
        builder.Add(Minimal("dup"));

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("dup"));
    }

    [Test]
    public void Empty_ability_name_is_rejected()
    {
        var kit = EffectTestKit.Create();
        var builder = new AbilityCatalogBuilder();
        builder.Add(new AbilityDef());

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("name"));
    }

    [Test]
    public void Unknown_referenced_effect_is_rejected()
    {
        var kit = EffectTestKit.Create();
        var ability = Minimal("caster");
        ability.Effects.Add(new AbilityEffectDef { EffectName = "missing" });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("missing"));
    }

    [Test]
    public void Unknown_cooldown_effect_is_rejected()
    {
        var kit = EffectTestKit.Create();
        var ability = Minimal("caster");
        ability.CooldownEffect = "nope";
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("nope"));
    }

    [Test]
    public void Cost_on_an_unregistered_attribute_is_rejected()
    {
        var kit = EffectTestKit.Create();
        var ability = Minimal("caster");
        ability.Costs.Add(new CostDef
        {
            AttributeId = 250,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(1) },
        });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Fixed_level_rule_beyond_the_effects_max_level_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(LeveledBolt("bolt"));
        var ability = Minimal("caster");
        // The ability's own MaxLevel is irrelevant here — the bound is the referenced effect's MaxLevel (5).
        ability.MaxLevel = 30;
        ability.Effects.Add(new AbilityEffectDef
        {
            EffectName = "bolt", LevelRule = ChainLevelRule.Fixed, FixedLevel = 9,
        });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("FixedLevel"));
    }

    [Test]
    public void Fixed_level_rule_within_the_effects_max_level_is_accepted()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(LeveledBolt("bolt"));
        var ability = Minimal("caster");
        ability.Effects.Add(new AbilityEffectDef
        {
            EffectName = "bolt", LevelRule = ChainLevelRule.Fixed, FixedLevel = 5,
        });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        var catalog = builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        Assert.That(catalog.Count, Is.EqualTo(1));
    }

    [Test]
    public void Fixed_level_rule_below_one_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("bolt"));
        var ability = Minimal("caster");
        ability.Effects.Add(new AbilityEffectDef
        {
            EffectName = "bolt", LevelRule = ChainLevelRule.Fixed, FixedLevel = 0,
        });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("FixedLevel"));
    }

    [Test]
    public void Unknown_selector_tag_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("bolt"));
        var ability = Minimal("caster");
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt", SelectorTag = "selector.nope" });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("selector.nope"));
    }

    [Test]
    public void Event_selector_on_an_ability_effect_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("bolt"));
        var ability = Minimal("caster");
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt", SelectorTag = BuiltInSelectors.EventSourceTag });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("only meaningful on an OnEvent"));
    }
}
