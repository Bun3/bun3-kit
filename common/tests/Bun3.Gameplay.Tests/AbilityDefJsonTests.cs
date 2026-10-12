#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Abilities.Catalog;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilityDefJsonTests
{
    private static readonly AttributeRegistry Attributes = EffectTestKit.BuildAttributeRegistry();

    private static List<AbilityDef> Load(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return AbilityDefJson.Load(stream, Attributes);
    }

    [Test]
    public void Target_fields_are_read()
    {
        var abilities = Load("""
        {
          "schemaVersion": 1,
          "abilities": [
            {
              "name": "execute",
              "target": "Required",
              "targetRequiredTags": ["state.armed"],
              "targetBlockedTags": ["state.silenced"],
              "targetConditions": [
                { "left": { "attribute": "Hp" }, "op": "Less", "right": { "constant": "30" } }
              ],
              "effects": [
                { "effect": "FireBolt", "origin": "Target" },
                { "effect": "FireBolt" }
              ]
            }
          ]
        }
        """);

        var ability = abilities[0];
        Assert.That(ability.Target, Is.EqualTo(AbilityTargetMode.Required));
        Assert.That(ability.TargetRequiredTags, Is.EqualTo(new[] { "state.armed" }));
        Assert.That(ability.TargetBlockedTags, Is.EqualTo(new[] { "state.silenced" }));
        Assert.That(ability.TargetConditions.Count, Is.EqualTo(1));
        Assert.That(ability.TargetConditions[0].Op, Is.EqualTo(ComparisonOp.Less));
        Assert.That(ability.Effects[0].Origin, Is.EqualTo(AbilityEffectOrigin.Target));
        Assert.That(ability.Effects[1].Origin, Is.EqualTo(AbilityEffectOrigin.Caster));
    }

    [Test]
    public void Full_ability_round_trips_every_field()
    {
        var abilities = Load("""
        {
          "schemaVersion": 1,
          "abilities": [
            {
              "name": "fireball",
              "maxLevel": 3,
              "assetTags": ["effect.fire.bolt"],
              "requiredTags": ["state.armed"],
              "blockedTags": ["cooldown.fireball"],
              "activationConditions": [
                { "left": { "attribute": "Mp" }, "op": "GreaterOrEqual", "right": { "constant": "10" } }
              ],
              "costs": [ { "attribute": "Mp", "magnitude": { "base": { "constant": "10" } } } ],
              "cooldownEffect": "Cooldown.Fireball",
              "effects": [
                { "effect": "FireBolt", "selector": "selector.event.source",
                  "selectorParams": ["2"], "level": 2 }
              ]
            }
          ]
        }
        """);

        Assert.That(abilities.Count, Is.EqualTo(1));
        var ability = abilities[0];
        Assert.That(ability.Name, Is.EqualTo("fireball"));
        Assert.That(ability.MaxLevel, Is.EqualTo(3));
        Assert.That(ability.AssetTags, Is.EqualTo(new[] { "effect.fire.bolt" }));
        Assert.That(ability.RequiredTags, Is.EqualTo(new[] { "state.armed" }));
        Assert.That(ability.BlockedTags, Is.EqualTo(new[] { "cooldown.fireball" }));
        Assert.That(ability.ActivationConditions.Count, Is.EqualTo(1));
        Assert.That(ability.ActivationConditions[0].Op, Is.EqualTo(ComparisonOp.GreaterOrEqual));
        Assert.That(ability.Costs.Count, Is.EqualTo(1));
        Assert.That(ability.Costs[0].AttributeId, Is.EqualTo(EffectTestKit.Mp));
        Assert.That(ability.Costs[0].Magnitude.Base!.Value.Value, Is.EqualTo((BigNum)10));
        Assert.That(ability.CooldownEffect, Is.EqualTo("Cooldown.Fireball"));
        Assert.That(ability.Effects.Count, Is.EqualTo(1));
        Assert.That(ability.Effects[0].EffectName, Is.EqualTo("FireBolt"));
        Assert.That(ability.Effects[0].SelectorTag, Is.EqualTo("selector.event.source"));
        Assert.That(ability.Effects[0].SelectorParams, Is.EqualTo(new[] { (BigNum)2 }));
        Assert.That(ability.Effects[0].LevelRule, Is.EqualTo(ChainLevelRule.Fixed));
        Assert.That(ability.Effects[0].FixedLevel, Is.EqualTo(2));
    }

    [Test]
    public void Minimal_ability_uses_defaults()
    {
        var abilities = Load("""{ "schemaVersion": 1, "abilities": [ { "name": "a" } ] }""");

        Assert.That(abilities[0].MaxLevel, Is.Zero);
        Assert.That(abilities[0].Costs, Is.Empty);
        Assert.That(abilities[0].CooldownEffect, Is.Null);
        Assert.That(abilities[0].Effects, Is.Empty);
    }

    // The ability effect's level rule shares EffectDefJson's chain-edge "level" shape (Inherit or a
    // fixed integer) rather than the old split levelRule/fixedLevel fields.
    [Test]
    public void Ability_effect_split_level_fields_are_rejected()
    {
        var ex = Assert.Throws<TagCatalogException>(() => Load("""
            { "schemaVersion": 1, "abilities": [ { "name": "a",
              "effects": [ { "effect": "FireBolt", "levelRule": "Fixed", "fixedLevel": 2 } ] } ] }
            """));
        Assert.That(ex!.Message, Does.Contain("levelRule"));
    }

    [Test]
    public void Unknown_key_is_rejected_with_line_and_position()
    {
        var ex = Assert.Throws<TagCatalogException>(
            () => Load("""{ "schemaVersion": 1, "abilities": [ { "name": "a", "bogus": 1 } ] }"""));
        Assert.That(ex!.LineNumber, Is.GreaterThan(0));
        Assert.That(ex.Message, Does.Contain("bogus"));
    }

    [Test]
    public void Unknown_attribute_name_in_a_cost_is_rejected()
    {
        var ex = Assert.Throws<TagCatalogException>(() => Load("""
            { "schemaVersion": 1, "abilities": [ { "name": "a",
              "costs": [ { "attribute": "Nope", "magnitude": { "base": { "constant": "1" } } } ] } ] }
            """));
        Assert.That(ex!.LineNumber, Is.GreaterThan(0));
        Assert.That(ex.Message, Does.Contain("Nope"));
    }

    [Test]
    public void Unsupported_schema_version_is_rejected()
    {
        var ex = Assert.Throws<TagCatalogException>(
            () => Load("""{ "schemaVersion": 99, "abilities": [] }"""));
        Assert.That(ex!.Message, Does.Contain("schemaVersion"));
    }

    // A fireball ability authored in JSON, run through AbilityCatalogBuilder.Build, proves the loaded
    // graph is actually buildable (not merely parsed) -- the same catalog-build path a C#-built AbilityDef goes through.
    [Test]
    public void Json_loaded_ability_builds_through_AbilityCatalogBuilder()
    {
        var abilities = Load("""
        {
          "schemaVersion": 1,
          "abilities": [
            {
              "name": "fireball",
              "maxLevel": 3,
              "assetTags": ["effect.fire.bolt"],
              "requiredTags": ["state.armed"],
              "blockedTags": ["cooldown.fireball"],
              "activationConditions": [
                { "left": { "attribute": "Mp" }, "op": "GreaterOrEqual", "right": { "constant": "10" } }
              ],
              "costs": [ { "attribute": "Mp", "magnitude": { "base": { "constant": "10" } } } ],
              "cooldownEffect": "Cooldown.Fireball",
              "effects": [ { "effect": "FireBolt", "level": "Inherit" } ]
            }
          ]
        }
        """);

        var kit = EffectTestKit.Create();
        var cooldown = EffectTestKit.MinimalDuration("Cooldown.Fireball", ticks: 10);
        cooldown.GrantedTags.Add("cooldown.fireball");
        kit.AddEffect(cooldown);
        kit.AddEffect(EffectTestKit.MinimalInstant("FireBolt"));

        var builder = new AbilityCatalogBuilder();
        foreach (var ability in abilities) builder.Add(ability);
        var catalog = builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        Assert.That(catalog.Count, Is.EqualTo(1));
        Assert.That(catalog.GetRequiredId("fireball"), Is.EqualTo(0));
    }
}
