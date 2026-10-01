#nullable enable
using System.IO;
using System.Text;
using Bun3.Gameplay.Catalog;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class ArchetypeDefJsonTests
{
    private static System.Collections.Generic.List<ArchetypeDef> Load(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ArchetypeDefJson.Load(stream, EffectTestKit.BuildAttributeRegistry());
    }

    [Test]
    public void Archetype_fields_are_read_by_attribute_name()
    {
        var archetypes = Load("""
        {
          "schemaVersion": 1,
          "archetypes": [
            {
              "name": "warrior",
              "attributes": { "MaxHp": "100", "Hp": "80" },
              "abilities": [ { "ability": "slash", "level": 2 }, { "ability": "kick" } ]
            }
          ]
        }
        """);

        var warrior = archetypes[0];
        Assert.That(warrior.Name, Is.EqualTo("warrior"));
        Assert.That(warrior.Attributes.Count, Is.EqualTo(2));
        Assert.That(warrior.Attributes[0].AttributeId, Is.EqualTo(EffectTestKit.MaxHp));
        Assert.That(warrior.Attributes[0].Base, Is.EqualTo((BigNum)100));
        Assert.That(warrior.Attributes[1].AttributeId, Is.EqualTo(EffectTestKit.Hp));
        Assert.That(warrior.Abilities[0].AbilityName, Is.EqualTo("slash"));
        Assert.That(warrior.Abilities[0].Level, Is.EqualTo(2));
        Assert.That(warrior.Abilities[1].Level, Is.EqualTo(1), "level defaults to 1");
    }

    [Test]
    public void An_unknown_attribute_name_is_rejected()
    {
        Assert.That(
            () => Load("""{ "schemaVersion": 1, "archetypes": [ { "name": "a", "attributes": { "Mana": "1" } } ] }"""),
            Throws.InstanceOf<TagCatalogException>().With.Message.Contains("Mana"));
    }

    [Test]
    public void A_numeric_base_is_rejected_because_bignums_are_strings()
    {
        Assert.That(
            () => Load("""{ "schemaVersion": 1, "archetypes": [ { "name": "a", "attributes": { "Hp": 1 } } ] }"""),
            Throws.InstanceOf<TagCatalogException>());
    }
}
