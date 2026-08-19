#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilityCatalogBuilderTests
{
    private static AbilityDef Minimal(string name) => new AbilityDef { Name = name };

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
    public void Fixed_level_rule_beyond_max_level_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("bolt"));
        var ability = Minimal("caster");
        ability.MaxLevel = 3;
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
            Throws.InstanceOf<Exception>());
    }
}
