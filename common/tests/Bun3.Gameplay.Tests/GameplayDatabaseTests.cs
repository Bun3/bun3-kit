#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class GameplayDatabaseTests
{
    [Test]
    public void Build_compiles_every_catalog_from_definitions()
    {
        var kit = EffectTestKit.Create();
        var effects = new List<EffectDef> { EffectTestKit.MinimalInstant("bolt") };
        var slash = new AbilityDef { Name = "slash", MaxLevel = 1 };
        slash.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
        var warrior = new ArchetypeDef { Name = "warrior" };
        warrior.Abilities.Add(new ArchetypeAbilityDef { AbilityName = "slash" });

        var database = GameplayDatabase.Build(
            kit.TagCatalog, kit.Seams, kit.Attributes, effects, new[] { slash }, new[] { warrior });

        Assert.That(database.Effects.GetRequiredId("bolt"), Is.Zero);
        Assert.That(database.Abilities.GetRequiredId("slash"), Is.Zero);
        Assert.That(database.Archetypes.GetRequiredId("warrior"), Is.Zero);
        Assert.That(database.Tags, Is.SameAs(kit.TagCatalog));
        Assert.That(database.Attributes, Is.SameAs(kit.Attributes));
        Assert.That(database.Seams, Is.SameAs(kit.Seams));
    }
}
