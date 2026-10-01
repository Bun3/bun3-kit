#nullable enable
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class ArchetypeCatalogBuilderTests
{
    private static AbilityCatalog BuildAbilities(EffectTestKit kit)
    {
        kit.AddEffect(EffectTestKit.MinimalInstant("bolt"));
        var builder = new AbilityCatalogBuilder();
        var slash = new AbilityDef { Name = "slash", MaxLevel = 3 };
        slash.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
        builder.Add(slash);
        return builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
    }

    private static ArchetypeDef Warrior()
    {
        var def = new ArchetypeDef { Name = "warrior" };
        def.Attributes.Add(new ArchetypeAttributeDef { AttributeId = EffectTestKit.Hp, Base = 80 });
        def.Attributes.Add(new ArchetypeAttributeDef { AttributeId = EffectTestKit.MaxHp, Base = 100 });
        def.Abilities.Add(new ArchetypeAbilityDef { AbilityName = "slash", Level = 2 });
        return def;
    }

    private static ArchetypeCatalog Build(EffectTestKit kit, params ArchetypeDef[] defs)
    {
        var abilities = BuildAbilities(kit);
        var builder = new ArchetypeCatalogBuilder();
        foreach (var def in defs) builder.Add(def);
        return builder.Build(kit.Attributes, abilities);
    }

    [Test]
    public void Build_resolves_names_and_orders_bases_by_clamp_evaluation_order()
    {
        var kit = EffectTestKit.Create();
        var catalog = Build(kit, Warrior());

        var id = catalog.GetRequiredId("warrior");
        Assert.That(catalog.Count, Is.EqualTo(1));
        Assert.That(catalog.GetName(id), Is.EqualTo("warrior"));
        Assert.That(catalog.TryGetId("nope", out _), Is.False);

        var compiled = catalog.GetArchetype(id);
        Assert.That(compiled.AttributeIds, Is.EqualTo(new[] { EffectTestKit.MaxHp, EffectTestKit.Hp }), "MaxHp bounds Hp, so it comes first");
        Assert.That(compiled.Bases, Is.EqualTo(new BigNum[] { 100, 80 }));
        Assert.That(compiled.AbilityLevels, Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void Duplicate_or_empty_archetype_names_are_rejected()
    {
        Assert.That(() => Build(EffectTestKit.Create(), Warrior(), Warrior()),
            Throws.InvalidOperationException.With.Message.Contains("warrior"));
        Assert.That(() => Build(EffectTestKit.Create(), new ArchetypeDef()), Throws.InvalidOperationException);
    }

    [Test]
    public void An_unregistered_or_repeated_attribute_is_rejected()
    {
        var unregistered = Warrior();
        unregistered.Attributes.Add(new ArchetypeAttributeDef { AttributeId = 99, Base = 1 });
        Assert.That(() => Build(EffectTestKit.Create(), unregistered),
            Throws.InvalidOperationException.With.Message.Contains("warrior").And.Message.Contains("99"));

        var repeated = Warrior();
        repeated.Attributes.Add(new ArchetypeAttributeDef { AttributeId = EffectTestKit.Hp, Base = 1 });
        Assert.That(() => Build(EffectTestKit.Create(), repeated),
            Throws.InvalidOperationException.With.Message.Contains("warrior").And.Message.Contains("Hp"));
    }

    [Test]
    public void Unknown_repeated_or_out_of_range_abilities_are_rejected()
    {
        var unknown = Warrior();
        unknown.Abilities.Add(new ArchetypeAbilityDef { AbilityName = "fly", Level = 1 });
        Assert.That(() => Build(EffectTestKit.Create(), unknown),
            Throws.InvalidOperationException.With.Message.Contains("fly"));

        var repeated = Warrior();
        repeated.Abilities.Add(new ArchetypeAbilityDef { AbilityName = "slash", Level = 1 });
        Assert.That(() => Build(EffectTestKit.Create(), repeated),
            Throws.InvalidOperationException.With.Message.Contains("slash"));

        var tooHigh = Warrior();
        tooHigh.Abilities[0].Level = 4;
        Assert.That(() => Build(EffectTestKit.Create(), tooHigh),
            Throws.InvalidOperationException.With.Message.Contains("MaxLevel"));

        var zero = Warrior();
        zero.Abilities[0].Level = 0;
        Assert.That(() => Build(EffectTestKit.Create(), zero), Throws.InvalidOperationException);
    }
}
