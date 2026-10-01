#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Abilities.Catalog;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Catalog;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Effects.Catalog;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class GameplayCatalogBinaryTests
{
    private static (EffectTestKit Kit, AbilityCatalog Abilities) BuildSource()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("bolt"));
        kit.AddEffect(EffectTestKit.MinimalDuration("burn", ticks: 4));

        var ability = new AbilityDef { Name = "fireball", MaxLevel = 2 };
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);
        return (kit, builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog));
    }

    [Test]
    public void Round_trip_preserves_names_and_ids()
    {
        var (kit, abilities) = BuildSource();
        using var stream = new MemoryStream();
        GameplayCatalogBinary.Write(stream, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        stream.Position = 0;

        var (loadedEffects, loadedAbilities) =
            GameplayCatalogBinary.Load(stream, kit.TagCatalog, kit.Seams, kit.Attributes);

        Assert.That(loadedEffects.Count, Is.EqualTo(kit.Catalog.Count));
        Assert.That(loadedEffects.GetRequiredId("bolt"), Is.EqualTo(kit.Catalog.GetRequiredId("bolt")));
        Assert.That(loadedEffects.GetRequiredId("burn"), Is.EqualTo(kit.Catalog.GetRequiredId("burn")));
        Assert.That(loadedAbilities.Count, Is.EqualTo(1));
        Assert.That(loadedAbilities.GetRequiredId("fireball"), Is.EqualTo(abilities.GetRequiredId("fireball")));
    }

    [Test]
    public void Tag_catalog_fingerprint_mismatch_is_rejected()
    {
        var (kit, abilities) = BuildSource();
        using var stream = new MemoryStream();
        GameplayCatalogBinary.Write(stream, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        stream.Position = 0;

        var otherKit = EffectTestKit.CreateWithExtraTag("state.extra");

        Assert.That(
            () => GameplayCatalogBinary.Load(stream, otherKit.TagCatalog, kit.Seams, kit.Attributes),
            Throws.InstanceOf<TagCatalogCompatibilityException>());
    }

    [Test]
    public void Truncated_payload_is_rejected()
    {
        var (kit, abilities) = BuildSource();
        using var full = new MemoryStream();
        GameplayCatalogBinary.Write(full, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        var bytes = full.ToArray();

        using var truncated = new MemoryStream(bytes, 0, bytes.Length - 4);

        Assert.That(
            () => GameplayCatalogBinary.Load(truncated, kit.TagCatalog, kit.Seams, kit.Attributes),
            Throws.InstanceOf<TagCatalogFormatException>());
    }

    [Test]
    public void Corrupted_byte_fails_the_checksum()
    {
        var (kit, abilities) = BuildSource();
        using var full = new MemoryStream();
        GameplayCatalogBinary.Write(full, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        var bytes = full.ToArray();
        bytes[bytes.Length - 1] ^= 0xFF;

        using var corrupted = new MemoryStream(bytes);

        Assert.That(
            () => GameplayCatalogBinary.Load(corrupted, kit.TagCatalog, kit.Seams, kit.Attributes),
            Throws.InstanceOf<TagCatalogFormatException>());
    }

    [Test]
    public void Attribute_registry_signature_mismatch_is_rejected()
    {
        var (kit, abilities) = BuildSource();
        using var stream = new MemoryStream();
        GameplayCatalogBinary.Write(stream, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        stream.Position = 0;

        var otherAttributes = new AttributeRegistryBuilder();
        otherAttributes.Register(EffectTestKit.MaxHp, min: Operand.Constant(1));
        otherAttributes.Register(EffectTestKit.Hp, min: Operand.Constant(0), max: Operand.Attribute(EffectTestKit.MaxHp));
        otherAttributes.Register(EffectTestKit.Attack, min: Operand.Constant(0));
        otherAttributes.Register(EffectTestKit.Mp);
        otherAttributes.Register(EffectTestKit.Resistance, min: Operand.Constant(0));

        Assert.That(
            () => GameplayCatalogBinary.Load(stream, kit.TagCatalog, kit.Seams, otherAttributes.Build()),
            Throws.InstanceOf<TagCatalogCompatibilityException>());
    }

    // Reflection walk over every member of every compiled type. It is the guard against silently
    // dropping a field from the format: a member added later that Write does not persist round-trips
    // to its default value and fails here.
    [Test]
    public void Round_trip_preserves_every_compiled_member()
    {
        var (kit, abilities) = BuildRichSource();
        using var stream = new MemoryStream();
        GameplayCatalogBinary.Write(stream, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        stream.Position = 0;

        var (loadedEffects, loadedAbilities) =
            GameplayCatalogBinary.Load(stream, kit.TagCatalog, kit.Seams, kit.Attributes);

        Assert.That(loadedEffects.Count, Is.EqualTo(kit.Catalog.Count));
        Assert.That(loadedEffects.BuildWarnings, Is.EqualTo(kit.Catalog.BuildWarnings));
        for (var id = 0; id < kit.Catalog.Count; id++)
        {
            AssertDeepEqual(
                typeof(object), GetEffect(kit.Catalog, id), GetEffect(loadedEffects, id), $"effect[{id}]");
        }

        Assert.That(loadedAbilities.Count, Is.EqualTo(abilities.Count));
        for (var id = 0; id < abilities.Count; id++)
        {
            AssertDeepEqual(
                typeof(object), GetAbility(abilities, id), GetAbility(loadedAbilities, id), $"ability[{id}]");
        }
    }

    [Test]
    public void Unregistered_seam_tag_is_rejected()
    {
        var (kit, abilities) = BuildRichSource();
        using var stream = new MemoryStream();
        GameplayCatalogBinary.Write(stream, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        stream.Position = 0;

        var emptySeams = new SeamRegistryBuilder().Build(kit.TagCatalog);
        var missing = kit.Tag("calc.magnitude.x");

        Assert.That(
            () => GameplayCatalogBinary.Load(stream, kit.TagCatalog, emptySeams, kit.Attributes),
            Throws.InstanceOf<TagCatalogCompatibilityException>()
                .With.Message.Contains("magnitude calc")
                .And.Message.Contains(missing.Index.ToString()));
    }

    // Loading must grow with the level table itself, not with per-entry diagnostic label strings.
    [Test]
    public void Load_allocation_per_level_table_entry_is_bounded()
    {
        var small = WriteLevelTableCatalog(levels: 2000);
        var large = WriteLevelTableCatalog(levels: 4000);
        MeasureLoad(small);   // warm-up: JIT and static init

        var perEntry = (MeasureLoad(large) - MeasureLoad(small)) / 2000.0;

        // ~60 today (payload buffering plus the BigNum itself); per-entry label strings made it ~220.
        Assert.That(perEntry, Is.LessThan(100), $"{perEntry:F1} bytes allocated per level-table entry");
    }

    private static (byte[] Bytes, EffectTestKit Kit) WriteLevelTableCatalog(int levels)
    {
        var kit = EffectTestKit.Create();
        var values = new List<BigNum>(levels);
        for (var level = 1; level <= levels; level++) values.Add(level);
        var deep = EffectTestKit.MinimalInfinite("deep");
        deep.MaxLevel = levels;
        deep.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { PerLevelValues = values },
        });
        kit.AddEffect(deep);

        var abilities = new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        using var stream = new MemoryStream();
        GameplayCatalogBinary.Write(stream, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        return (stream.ToArray(), kit);
    }

    private static long MeasureLoad((byte[] Bytes, EffectTestKit Kit) source)
    {
        using var stream = new MemoryStream(source.Bytes);
        var before = GC.GetAllocatedBytesForCurrentThread();
        GameplayCatalogBinary.Load(stream, source.Kit.TagCatalog, source.Kit.Seams, source.Kit.Attributes);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public void Binary_and_json_paths_produce_the_same_scenario_hash()
    {
        var kit = EffectScenario.ScenarioKit();
        using var stream = new MemoryStream();
        GameplayCatalogBinary.Write(stream, kit.Effects, kit.Abilities, kit.Tags, kit.Attributes);
        stream.Position = 0;

        var (loadedEffects, loadedAbilities) =
            GameplayCatalogBinary.Load(stream, kit.Tags, kit.Seams, kit.Attributes);

        var effectBuilder = new EffectCatalogBuilder();
        using (var effectStream = new MemoryStream(Encoding.UTF8.GetBytes(ScenarioEffectsJson)))
        {
            foreach (var def in EffectDefJson.Load(effectStream, EffectScenario.ScenarioAttributeNames))
            {
                effectBuilder.Add(def);
            }
        }

        var jsonEffects = effectBuilder.Build(kit.Tags, kit.Seams, kit.Attributes);

        var abilityBuilder = new AbilityCatalogBuilder();
        using (var abilityStream = new MemoryStream(Encoding.UTF8.GetBytes(ScenarioAbilitiesJson)))
        {
            foreach (var def in AbilityDefJson.Load(abilityStream, EffectScenario.ScenarioAttributeNames))
            {
                abilityBuilder.Add(def);
            }
        }

        var jsonAbilities = abilityBuilder.Build(kit.Tags, kit.Seams, kit.Attributes, jsonEffects);

        var builderHash = EffectScenario.RunHash(kit.Effects, kit.Abilities, kit.Tags, seed: 20260820);
        var binaryHash = EffectScenario.RunHash(loadedEffects, loadedAbilities, kit.Tags, seed: 20260820);
        var jsonHash = EffectScenario.RunHash(jsonEffects, jsonAbilities, kit.Tags, seed: 20260820);

        Assert.That(binaryHash, Is.EqualTo(builderHash));
        Assert.That(jsonHash, Is.EqualTo(builderHash));
    }

    // JSON mirror of EffectScenario's AddScenarioEffects -- same names, same order (so ids line up),
    // same fields.
    private const string ScenarioEffectsJson = """
    {
      "schemaVersion": 1,
      "effects": [
        {
          "name": "Strike", "duration": { "type": "Instant" },
          "modifiers": [ { "attribute": "Hp", "op": "Add", "magnitude": { "constant": "-8" } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [], "assetTags": [], "immunityTags": [], "chains": [],
          "raiseEvents": [ { "tag": "event.hit", "attribute": "Hp" } ]
        },
        {
          "name": "StrikeBack", "duration": { "type": "Instant" },
          "modifiers": [ { "attribute": "Hp", "op": "Add", "magnitude": { "constant": "-4" } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [], "assetTags": [], "immunityTags": [], "chains": []
        },
        {
          "name": "Retaliate", "duration": { "type": "Infinite" },
          "modifiers": [], "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [], "assetTags": [], "immunityTags": [],
          "chains": [ { "trigger": "OnEvent", "effect": "StrikeBack", "eventTag": "event.hit",
                        "selector": "selector.event.source", "level": "Inherit" } ]
        },
        {
          "name": "Cooldown", "duration": { "type": "Duration", "ticks": 15 },
          "modifiers": [], "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [ "cooldown.strike" ], "assetTags": [], "immunityTags": [], "chains": []
        },
        {
          "name": "Regen", "duration": { "type": "Duration", "ticks": 40, "periodTicks": 4 },
          "modifiers": [ { "attribute": "Hp", "op": "Add", "magnitude": { "constant": "3" } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [], "assetTags": [], "immunityTags": [], "chains": []
        },
        {
          "name": "Buff", "duration": { "type": "Duration", "ticks": 25 },
          "stack": { "maxStack": 4, "onReapply": "AddStack", "addStackCount": 1,
                     "refreshDurationOnReapply": true, "onExpiration": "ClearAll", "onOverflow": "Deny" },
          "modifiers": [ { "attribute": "Attack", "op": "Add", "magnitude": { "constant": "2" } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [], "assetTags": [], "immunityTags": [], "chains": []
        },
        {
          "name": "Reflect", "duration": { "type": "Infinite" },
          "modifiers": [ { "attribute": "Attack", "op": "Add", "magnitude": { "constant": "6" } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [], "assetTags": [], "immunityTags": [], "chains": []
        },
        {
          "name": "Mark", "duration": { "type": "Infinite" },
          "modifiers": [], "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [], "assetTags": [], "immunityTags": [], "chains": [],
          "grantedAbilities": [ "Ward" ]
        }
      ]
    }
    """;

    // JSON mirror of EffectScenario's AddScenarioAbilities -- same names, same order.
    private const string ScenarioAbilitiesJson = """
    {
      "schemaVersion": 1,
      "abilities": [
        {
          "name": "Slash", "maxLevel": 1,
          "requiredTags": [ "state.armed" ], "blockedTags": [ "cooldown.strike" ],
          "costs": [ { "attribute": "Mp", "magnitude": { "base": { "constant": "5" } } } ],
          "cooldownEffect": "Cooldown",
          "effects": [ { "effect": "Strike" } ]
        },
        { "name": "Ward", "maxLevel": 1 }
      ]
    }
    """;

    // A catalog exercising every compiled member: seam-backed magnitudes and executions, level tables,
    // every tag list, chains including OnEvent, stack overflow, DR, duration scaling, and raised events.
    private static (EffectTestKit Kit, AbilityCatalog Abilities) BuildRichSource()
    {
        var kit = EffectTestKit.Create();
        kit.RegisterMagnitudeCalc("calc.magnitude.x", new ConstantMagnitude());
        kit.RegisterExecutionCalc("calc.execution.dmg", new EffectTestKit.SubtractHpCalc());
        kit.RegisterSelector("selector.everyone", new EffectTestKit.AllTargetsSelector());
        kit.RegisterSelector("selector.team", new EffectTestKit.AllTargetsSelector());

        var bolt = EffectTestKit.MinimalInstant("bolt");
        bolt.MaxLevel = 3;
        bolt.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x" },
            ScaleWithStack = false,
        });
        bolt.ApplicationConditions.Add(new ConditionDef
        {
            Left = Operand.SourceAttribute(EffectTestKit.Attack),
            Op = ComparisonOp.GreaterOrEqual,
            Right = Operand.Constant(1),
        });
        bolt.AssetTags.Add("effect.fire.bolt");
        bolt.RaiseEvents.Add(new RaiseEventDef { Tag = "event.damage", AttributeId = EffectTestKit.Hp });
        kit.AddEffect(bolt);

        var cooldown = EffectTestKit.MinimalDuration("cooldown", ticks: 30);
        cooldown.GrantedTags.Add("cooldown.fireball");
        cooldown.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack,
            Op = AttributeModifierOp.Multiply,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(-5, -1)) },
        });
        cooldown.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Mp,
            Op = AttributeModifierOp.Override,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(0) },
        });
        kit.AddEffect(cooldown);

        var burn = new EffectDef
        {
            Name = "burn",
            MaxLevel = 3,
            DurationType = EffectDurationType.Duration,
            PeriodTicks = 2,
            DurationPerLevel = new List<BigNum> { 10, 20, 30 },
            DurationScale = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(15, -1)) },
            ChanceToApply = new MagnitudeDef
            {
                PerLevelValues = new List<BigNum>
                {
                    BigNum.FromParts(5, -1), BigNum.FromParts(75, -2), BigNum.One,
                },
                Tail = LevelTail.Extrapolate,
                ExtrapolateIncrement = BigNum.FromParts(1, -1),
            },
            DrCategory = "state.frozen",
            DrWindowTicks = 100,
            DrStageMultipliers = new List<BigNum> { BigNum.FromParts(5, -1), BigNum.Zero },
        };
        burn.Stack.MaxStack = 3;
        burn.Stack.OnReapply = StackReapply.AddStack;
        burn.Stack.AddStackCount = 2;
        burn.Stack.ResetPeriodOnReapply = true;
        burn.Stack.OnExpiration = StackExpiration.RemoveOneAndRefresh;
        burn.Stack.OnOverflow = StackOverflow.ApplyEffect;
        burn.Stack.OverflowEffectName = "bolt";
        burn.Stack.ClearStacksOnOverflow = true;
        burn.Stack.LevelFromStack = true;
        burn.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack,
            Op = AttributeModifierOp.Add,
            ScaleWithStack = true,
            Magnitude = new MagnitudeDef
            {
                PerLevelValues = new List<BigNum> { 1, 2, 3 },
                Tail = LevelTail.Extrapolate,
                ExtrapolateIncrement = 2,
            },
        });
        burn.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Resistance,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef
            {
                Base = Operand.Constant(2),
                PerLevel = Operand.Attribute(EffectTestKit.MaxHp, BigNum.FromParts(1, -2)),
            },
        });
        burn.Executions.Add(new ExecutionDef
        {
            CalcTag = "calc.execution.dmg",
            Inputs = new List<Operand> { Operand.Constant(3), Operand.SourceAttribute(EffectTestKit.Attack) },
        });
        burn.OngoingConditions.Add(new ConditionDef
        {
            Left = Operand.Attribute(EffectTestKit.Hp),
            Op = ComparisonOp.Greater,
            Right = Operand.Constant(0),
        });
        burn.GrantedTags.Add("state.chilled");
        burn.GrantedAbilities.Add("fireball");
        burn.AssetTags.Add("effect.frost");
        burn.ImmunityTags.Add("effect.magic.curse");
        burn.RemoveOnApplyTags.Add("effect.fire.bolt");
        burn.Chains.Add(new ChainEdgeDef
        {
            Trigger = ChainTrigger.OnApplication,
            EffectName = "bolt",
            SelectorTag = "selector.everyone",
            SelectorParams = new List<BigNum> { 1, BigNum.FromParts(25, -2) },
            LevelRule = ChainLevelRule.Fixed,
            FixedLevel = 2,
            Conditions =
            {
                new ConditionDef
                {
                    Left = Operand.Attribute(EffectTestKit.Mp),
                    Op = ComparisonOp.NotEqual,
                    Right = Operand.Constant(0),
                },
            },
        });
        burn.Chains.Add(new ChainEdgeDef
        {
            Trigger = ChainTrigger.OnEvent,
            EffectName = "bolt",
            EventTag = "event.damage.taken",
            Listen = ChainListen.AsSource,
            SelectorTag = "selector.event.source",
            LevelRule = ChainLevelRule.Inherit,
            Conditions =
            {
                new ConditionDef
                {
                    Left = Operand.EventMagnitude(),
                    Op = ComparisonOp.Less,
                    Right = Operand.Constant(100),
                },
            },
        });
        kit.AddEffect(burn);

        var fireball = new AbilityDef
        {
            Name = "fireball", MaxLevel = 2, CooldownEffect = "cooldown", Target = AbilityTargetMode.Required,
        };
        fireball.TargetRequiredTags.Add("state.armed");
        fireball.TargetBlockedTags.Add("state.silenced");
        fireball.TargetConditions.Add(new ConditionDef
        {
            Left = Operand.Attribute(EffectTestKit.Hp),
            Op = ComparisonOp.Less,
            Right = Operand.SourceAttribute(EffectTestKit.Attack),
        });
        fireball.AssetTags.Add("effect.fire.bolt");
        fireball.RequiredTags.Add("state.armed");
        fireball.BlockedTags.Add("cooldown.fireball");
        fireball.ActivationConditions.Add(new ConditionDef
        {
            Left = Operand.Attribute(EffectTestKit.Mp),
            Op = ComparisonOp.GreaterOrEqual,
            Right = Operand.Constant(10),
        });
        fireball.Costs.Add(new CostDef
        {
            AttributeId = EffectTestKit.Mp,
            Magnitude = new MagnitudeDef { PerLevelValues = new List<BigNum> { 10, 20 } },
        });
        fireball.Costs.Add(new CostDef
        {
            AttributeId = EffectTestKit.Hp,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x" },
        });
        fireball.Effects.Add(new AbilityEffectDef
        {
            EffectName = "burn",
            SelectorTag = "selector.team",
            SelectorParams = new List<BigNum> { 4 },
            LevelRule = ChainLevelRule.Fixed,
            FixedLevel = 1,
        });
        fireball.Effects.Add(new AbilityEffectDef { EffectName = "bolt", Origin = AbilityEffectOrigin.Target });

        var builder = new AbilityCatalogBuilder();
        builder.Add(fireball);
        return (kit, builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog));
    }

    private static object GetEffect(EffectCatalog catalog, int id) =>
        Invoke(catalog, "GetEffect", id);

    private static object GetAbility(AbilityCatalog catalog, int id) =>
        Invoke(catalog, "GetAbility", id);

    private static object Invoke(object target, string method, int id)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return info.Invoke(target, new object[] { id })!;
    }

    // Types compared by value; everything else is walked property by property.
    private static readonly Type[] LeafTypes =
    {
        typeof(string), typeof(BigNum), typeof(Operand), typeof(GameplayTag),
    };

    // Boundary of the guard: it walks properties only. Compiled types expose every member as a
    // get-only auto-property; a public field or an indexer added later would be skipped or throw here.
    private static void AssertDeepEqual(Type declaredType, object? expected, object? actual, string path)
    {
        if (expected is null || actual is null)
        {
            Assert.That(actual, Is.EqualTo(expected), path);
            return;
        }

        // Seams are resolved from the SeamRegistry, never serialized — the same instance must come back.
        if (declaredType.IsInterface)
        {
            Assert.That(ReferenceEquals(expected, actual), Is.True, path + " must be the registered seam instance");
            return;
        }

        var type = expected.GetType();
        if (type.IsArray)
        {
            var expectedArray = (Array)expected;
            var actualArray = (Array)actual;
            Assert.That(actualArray.Length, Is.EqualTo(expectedArray.Length), path + ".Length");
            var elementType = type.GetElementType()!;
            for (var i = 0; i < expectedArray.Length; i++)
            {
                AssertDeepEqual(elementType, expectedArray.GetValue(i), actualArray.GetValue(i), $"{path}[{i}]");
            }

            return;
        }

        if (type.IsPrimitive || type.IsEnum || Array.IndexOf(LeafTypes, type) >= 0)
        {
            Assert.That(actual, Is.EqualTo(expected), path);
            return;
        }

        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(properties.Length, Is.GreaterThan(0), path + " exposes no members to compare");
        foreach (var property in properties)
        {
            AssertDeepEqual(
                property.PropertyType,
                property.GetValue(expected),
                property.GetValue(actual),
                $"{path}.{property.Name}");
        }
    }

    private sealed class ConstantMagnitude : IMagnitudeCalc
    {
        public BigNum Calculate(in MagnitudeContext ctx) => 7;
    }
}
