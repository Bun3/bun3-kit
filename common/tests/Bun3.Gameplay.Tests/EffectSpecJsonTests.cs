#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Effects.Catalog;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectSpecJsonTests
{
    // Name map aligned with EffectTestKit's attribute id constants.
    private static readonly Dictionary<string, ushort> AttributeNames = new Dictionary<string, ushort>
    {
        ["Hp"] = EffectTestKit.Hp,
        ["MaxHp"] = EffectTestKit.MaxHp,
        ["Mp"] = EffectTestKit.Mp,
        ["Attack"] = EffectTestKit.Attack,
    };

    private static List<EffectSpec> Load(string json, IReadOnlyDictionary<string, ushort>? attributeNames = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return EffectSpecJson.Load(stream, attributeNames ?? AttributeNames);
    }

    private const string ChillSampleJson = """
    {
      "schemaVersion": 1,
      "specs": [ {
        "name": "chill",
        "duration": { "type": "Duration", "ticks": 10, "periodTicks": 0 },
        "stack": { "maxStack": 3, "onReapply": "AddStack",
                   "onOverflow": "ApplyEffect", "overflowEffect": "frozen", "clearStacksOnOverflow": true },
        "modifiers": [ { "attribute": "Attack", "op": "Add",
                         "magnitude": { "constant": "-5" }, "scaleWithStack": true } ],
        "executions": [ { "calc": "calc.execution.dmg",
                          "inputs": [ { "attribute": "MaxHp", "coefficient": "0.5" } ] } ],
        "applicationConditions": [ { "left": { "attribute": "Hp" }, "op": "Less",
                                     "right": { "attribute": "MaxHp", "coefficient": "0.3" } } ],
        "ongoingConditions": [],
        "grantedTags": [ "state.chilled" ],
        "assetTags": [ "effect.frost" ],
        "immunityTags": [],
        "chains": [ { "trigger": "OnStackOverflow", "effect": "frozen" } ] } ] }
    """;

    [Test]
    public void Round_trip_parses_every_field_of_the_sample_spec()
    {
        var specs = Load(ChillSampleJson);
        Assert.That(specs, Has.Count.EqualTo(1));
        var chill = specs[0];

        Assert.That(chill.Name, Is.EqualTo("chill"));
        Assert.That(chill.DurationType, Is.EqualTo(EffectDurationType.Duration));
        Assert.That(chill.DurationTicks, Is.EqualTo(10));
        Assert.That(chill.PeriodTicks, Is.EqualTo(0));

        Assert.That(chill.Stack.MaxStack, Is.EqualTo(3));
        Assert.That(chill.Stack.OnReapply, Is.EqualTo(StackReapply.AddStack));
        Assert.That(chill.Stack.OnOverflow, Is.EqualTo(StackOverflow.ApplyEffect));
        Assert.That(chill.Stack.OverflowEffectName, Is.EqualTo("frozen"));
        Assert.That(chill.Stack.ClearStacksOnOverflow, Is.True);
        // Fields absent from JSON must keep the model defaults.
        Assert.That(chill.Stack.AddStackCount, Is.EqualTo(1));
        Assert.That(chill.Stack.RefreshDurationOnReapply, Is.True);
        Assert.That(chill.Stack.OnExpiration, Is.EqualTo(StackExpiration.ClearAll));

        Assert.That(chill.Modifiers, Has.Count.EqualTo(1));
        var modifier = chill.Modifiers[0];
        Assert.That(modifier.AttributeId, Is.EqualTo(EffectTestKit.Attack));
        Assert.That(modifier.Op, Is.EqualTo(AttributeModifierOp.Add));
        Assert.That(modifier.ScaleWithStack, Is.True);
        Assert.That(modifier.Magnitude.CalcTag, Is.Null);
        Assert.That(modifier.Magnitude.PerLevel, Is.Null);
        Assert.That(modifier.Magnitude.Base, Is.EqualTo(Operand.Constant(BigNum.FromParts(-5, 0))));

        Assert.That(chill.Executions, Has.Count.EqualTo(1));
        Assert.That(chill.Executions[0].CalcTag, Is.EqualTo("calc.execution.dmg"));
        Assert.That(chill.Executions[0].Inputs, Has.Count.EqualTo(1));
        Assert.That(chill.Executions[0].Inputs[0],
            Is.EqualTo(Operand.Attribute(EffectTestKit.MaxHp, BigNum.FromParts(5, -1))));

        Assert.That(chill.ApplicationConditions, Has.Count.EqualTo(1));
        var condition = chill.ApplicationConditions[0];
        Assert.That(condition.Left, Is.EqualTo(Operand.Attribute(EffectTestKit.Hp)));
        Assert.That(condition.Op, Is.EqualTo(ComparisonOp.Less));
        Assert.That(condition.Right, Is.EqualTo(Operand.Attribute(EffectTestKit.MaxHp, BigNum.FromParts(3, -1))));
        Assert.That(chill.OngoingConditions, Is.Empty);

        Assert.That(chill.GrantedTags, Is.EqualTo(new[] { "state.chilled" }));
        Assert.That(chill.AssetTags, Is.EqualTo(new[] { "effect.frost" }));
        Assert.That(chill.ImmunityTags, Is.Empty);

        Assert.That(chill.Chains, Has.Count.EqualTo(1));
        var chain = chill.Chains[0];
        Assert.That(chain.Trigger, Is.EqualTo(ChainTrigger.OnStackOverflow));
        Assert.That(chain.EffectName, Is.EqualTo("frozen"));
        Assert.That(chain.SelectorTag, Is.Null);
        Assert.That(chain.SelectorParams, Is.Empty);
        Assert.That(chain.Conditions, Is.Empty);
        Assert.That(chain.LevelRule, Is.EqualTo(ChainLevelRule.Inherit));
        Assert.That(chain.FixedLevel, Is.EqualTo(0));
    }

    [Test]
    public void Unknown_field_throws_with_line_info()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "x", "duration": { "type": "Instant" }, "modifiers": [], "executions": [],
          "applicationConditions": [], "ongoingConditions": [], "grantedTags": [], "assetTags": [],
          "immunityTags": [], "chains": [], "bogus": 1 } ] }
        """;
        var ex = Assert.Throws<TagCatalogException>(() => Load(json));
        Assert.That(ex!.LineNumber, Is.GreaterThan(0));
        Assert.That(ex.Message, Does.Contain("bogus"));
    }

    [Test]
    public void BigNum_as_json_number_throws_with_line_info()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "x", "duration": { "type": "Instant" },
          "modifiers": [ { "attribute": "Hp", "op": "Add", "magnitude": { "constant": -5 } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [], "grantedTags": [],
          "assetTags": [], "immunityTags": [], "chains": [] } ] }
        """;
        var ex = Assert.Throws<TagCatalogException>(() => Load(json));
        Assert.That(ex!.LineNumber, Is.GreaterThan(0));
    }

    [Test]
    public void Unknown_enum_name_throws_with_line_info()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "x", "duration": { "type": "Bogus" }, "modifiers": [], "executions": [],
          "applicationConditions": [], "ongoingConditions": [], "grantedTags": [], "assetTags": [],
          "immunityTags": [], "chains": [] } ] }
        """;
        var ex = Assert.Throws<TagCatalogException>(() => Load(json));
        Assert.That(ex!.LineNumber, Is.GreaterThan(0));
    }

    [Test]
    public void Unknown_attribute_name_throws_with_line_info()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "x", "duration": { "type": "Instant" },
          "modifiers": [ { "attribute": "NoSuchAttribute", "op": "Add", "magnitude": { "constant": "-5" } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [], "grantedTags": [],
          "assetTags": [], "immunityTags": [], "chains": [] } ] }
        """;
        var ex = Assert.Throws<TagCatalogException>(() => Load(json));
        Assert.That(ex!.LineNumber, Is.GreaterThan(0));
        Assert.That(ex.Message, Does.Contain("NoSuchAttribute"));
    }

    // ---- Two-path equivalence: chill/frozen/poison catalog built in code vs loaded from JSON ----

    private const string TrioJson = """
    { "schemaVersion": 1, "specs": [
      { "name": "chill",
        "duration": { "type": "Duration", "ticks": 10, "periodTicks": 0 },
        "stack": { "maxStack": 3, "onReapply": "AddStack",
                   "onOverflow": "ApplyEffect", "overflowEffect": "frozen", "clearStacksOnOverflow": true },
        "modifiers": [ { "attribute": "Attack", "op": "Add", "magnitude": { "constant": "-5" }, "scaleWithStack": true } ],
        "executions": [], "applicationConditions": [], "ongoingConditions": [],
        "grantedTags": [], "assetTags": [], "immunityTags": [],
        "chains": [ { "trigger": "OnStackOverflow", "effect": "frozen" } ] },
      { "name": "frozen",
        "duration": { "type": "Duration", "ticks": 2, "periodTicks": 0 },
        "modifiers": [], "executions": [], "applicationConditions": [], "ongoingConditions": [],
        "grantedTags": [ "state.frozen" ], "assetTags": [], "immunityTags": [], "chains": [] },
      { "name": "poison",
        "duration": { "type": "Duration", "ticks": 6, "periodTicks": 2 },
        "modifiers": [ { "attribute": "Hp", "op": "Add", "magnitude": { "constant": "-10" }, "scaleWithStack": true } ],
        "executions": [], "applicationConditions": [], "ongoingConditions": [],
        "grantedTags": [], "assetTags": [], "immunityTags": [], "chains": [] } ] }
    """;

    private static void AddTrioByCode(EffectTestKit kit)
    {
        var chill = EffectTestKit.MinimalDuration("chill", ticks: 10);
        chill.Stack = new StackPolicy
        {
            MaxStack = 3, OnReapply = StackReapply.AddStack,
            OnOverflow = StackOverflow.ApplyEffect, OverflowEffectName = "frozen", ClearStacksOnOverflow = true,
        };
        chill.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-5) },
        });
        chill.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnStackOverflow, "frozen"));
        kit.AddSpec(chill);

        var frozen = EffectTestKit.MinimalDuration("frozen", ticks: 2);
        frozen.GrantedTags.Add("state.frozen");
        kit.AddSpec(frozen);

        var poison = EffectTestKit.MinimalDuration("poison", ticks: 6);
        poison.PeriodTicks = 2;
        poison.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-10) },
        });
        kit.AddSpec(poison);
    }

    private static void AddTrioByJson(EffectTestKit kit)
    {
        foreach (var spec in Load(TrioJson))
        {
            kit.AddSpec(spec);
        }
    }

    private static void RunScenario(EffectTestKit kit)
    {
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 100);

        var pipeline = kit.BuildPipeline();
        for (var i = 0; i < 4; i++)   // 4th reapply overflows the stack -> frozen
        {
            pipeline.EnqueueApply(kit.SpecId("chill"), kit.Attacker.Id, kit.Defender.Id);
            pipeline.Tick();
        }

        pipeline.EnqueueApply(kit.SpecId("poison"), kit.Attacker.Id, kit.Defender.Id);
        for (var i = 0; i < 20; i++)
        {
            pipeline.Tick();
        }
    }

    [Test]
    public void Code_built_and_json_loaded_catalogs_produce_identical_outcomes()
    {
        var codeKit = EffectTestKit.Create();
        AddTrioByCode(codeKit);
        RunScenario(codeKit);

        var jsonKit = EffectTestKit.Create();
        AddTrioByJson(jsonKit);
        RunScenario(jsonKit);

        Assert.That(jsonKit.Defender.Attributes.GetCurrent(EffectTestKit.Hp),
            Is.EqualTo(codeKit.Defender.Attributes.GetCurrent(EffectTestKit.Hp)));
        Assert.That(jsonKit.Defender.Attributes.GetCurrent(EffectTestKit.Attack),
            Is.EqualTo(codeKit.Defender.Attributes.GetCurrent(EffectTestKit.Attack)));
        Assert.That(jsonKit.Defender.ActiveEffectCount, Is.EqualTo(codeKit.Defender.ActiveEffectCount));
    }

    // ---- Level-table authoring notations (②③④): round trips and errors ----

    [Test]
    public void PerLevelValues_form_round_trips()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "x", "maxLevel": 3, "duration": { "type": "Instant" },
          "modifiers": [ { "attribute": "Attack", "op": "Add",
                           "magnitude": { "perLevelValues": ["10", "25", "70"] } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [], "grantedTags": [],
          "assetTags": [], "immunityTags": [], "chains": [] } ] }
        """;
        var spec = Load(json)[0];
        Assert.That(spec.MaxLevel, Is.EqualTo(3));
        var magnitude = spec.Modifiers[0].Magnitude;
        Assert.That(magnitude.PerLevelValues, Is.EqualTo(new List<BigNum> { 10, 25, 70 }));
        Assert.That(magnitude.Tail, Is.EqualTo(LevelTail.Clamp));
        Assert.That(magnitude.ExtrapolateIncrement, Is.EqualTo(BigNum.Zero));
    }

    [Test]
    public void Formula_form_round_trips_with_tail_and_increment()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "x", "maxLevel": 5, "duration": { "type": "Instant" },
          "modifiers": [ { "attribute": "Attack", "op": "Add",
                           "magnitude": { "formula": "x*x", "tail": "Extrapolate", "extrapolateIncrement": "9" } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [], "grantedTags": [],
          "assetTags": [], "immunityTags": [], "chains": [] } ] }
        """;
        var spec = Load(json)[0];
        var magnitude = spec.Modifiers[0].Magnitude;
        Assert.That(magnitude.Formula, Is.EqualTo("x*x"));
        Assert.That(magnitude.Tail, Is.EqualTo(LevelTail.Extrapolate));
        Assert.That(magnitude.ExtrapolateIncrement, Is.EqualTo((BigNum)9));
    }

    [Test]
    public void CurveKeys_form_round_trips()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "x", "maxLevel": 50, "duration": { "type": "Instant" },
          "modifiers": [ { "attribute": "Attack", "op": "Add",
                           "magnitude": { "curveKeys": [
                             { "level": 1, "value": "10" }, { "level": 50, "value": "400" } ] } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [], "grantedTags": [],
          "assetTags": [], "immunityTags": [], "chains": [] } ] }
        """;
        var spec = Load(json)[0];
        var keys = spec.Modifiers[0].Magnitude.CurveKeys;
        Assert.That(keys, Has.Count.EqualTo(2));
        Assert.That(keys![0].Level, Is.EqualTo(1));
        Assert.That(keys[0].Value, Is.EqualTo((BigNum)10));
        Assert.That(keys[1].Level, Is.EqualTo(50));
        Assert.That(keys[1].Value, Is.EqualTo((BigNum)400));
    }

    [Test]
    public void Magnitude_with_both_base_and_formula_throws()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "x", "maxLevel": 3, "duration": { "type": "Instant" },
          "modifiers": [ { "attribute": "Attack", "op": "Add",
                           "magnitude": { "base": { "constant": "1" }, "formula": "x" } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [], "grantedTags": [],
          "assetTags": [], "immunityTags": [], "chains": [] } ] }
        """;
        Assert.Throws<TagCatalogException>(() => Load(json));
    }

    // A formula spec built in code and the same spec loaded from JSON yield the same result (§15.1 notation ③ equivalence).
    [Test]
    public void Code_built_and_json_loaded_formula_spec_produce_identical_outcome()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "surge", "maxLevel": 5,
          "duration": { "type": "Duration", "ticks": 100, "periodTicks": 0 },
          "modifiers": [ { "attribute": "Attack", "op": "Add", "magnitude": { "formula": "x*x" } } ],
          "executions": [], "applicationConditions": [], "ongoingConditions": [], "grantedTags": [],
          "assetTags": [], "immunityTags": [], "chains": [] } ] }
        """;

        var codeSpec = EffectTestKit.MinimalDuration("surge", ticks: 100);
        codeSpec.MaxLevel = 5;
        codeSpec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Formula = "x*x" },
        });

        var codeKit = EffectTestKit.Create();
        codeKit.AddSpec(codeSpec);
        var codePipeline = codeKit.BuildPipeline();
        codeKit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);
        codePipeline.EnqueueApply(codeKit.SpecId("surge"), codeKit.Attacker.Id, codeKit.Defender.Id, level: 4);
        codePipeline.Tick();

        var jsonKit = EffectTestKit.Create();
        foreach (var spec in Load(json)) jsonKit.AddSpec(spec);
        var jsonPipeline = jsonKit.BuildPipeline();
        jsonKit.Defender.Attributes.SetBase(EffectTestKit.Attack, 0);
        jsonPipeline.EnqueueApply(jsonKit.SpecId("surge"), jsonKit.Attacker.Id, jsonKit.Defender.Id, level: 4);
        jsonPipeline.Tick();

        Assert.That(jsonKit.Defender.Attributes.GetCurrent(EffectTestKit.Attack),
            Is.EqualTo(codeKit.Defender.Attributes.GetCurrent(EffectTestKit.Attack)));
        Assert.That(jsonKit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)16)); // 4*4
    }

    // ---- T2/T3 new-field round trip ----

    [Test]
    public void T2_T3_new_fields_round_trip()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "kitchen_sink", "maxLevel": 3,
          "duration": { "type": "Duration", "ticks": 0, "periodTicks": 0 },
          "stack": { "maxStack": 3, "onReapply": "ExtendCapped", "onOverflow": "Deny",
                     "levelFromStack": true, "extendCapMultiplier": "1.5" },
          "modifiers": [], "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [], "assetTags": [ "effect.frost" ], "immunityTags": [], "chains": [],
          "removeOnApplyTags": [ "effect.frost" ],
          "chanceToApply": { "constant": "0.75" },
          "durationPerLevel": [ "10", "20", "30" ],
          "durationScale": { "constant": "2" },
          "drCategory": "effect.frost", "drWindowTicks": 100,
          "drStageMultipliers": [ "0.5", "0" ] } ] }
        """;
        var spec = Load(json)[0];

        Assert.That(spec.RemoveOnApplyTags, Is.EqualTo(new[] { "effect.frost" }));
        Assert.That(spec.ChanceToApply, Is.Not.Null);
        Assert.That(spec.ChanceToApply!.Base, Is.EqualTo(Operand.Constant(BigNum.FromParts(75, -2))));
        Assert.That(spec.DurationPerLevel, Is.EqualTo(new List<BigNum> { 10, 20, 30 }));
        Assert.That(spec.DurationScale!.Base, Is.EqualTo(Operand.Constant(BigNum.FromParts(2, 0))));
        Assert.That(spec.DrCategory, Is.EqualTo("effect.frost"));
        Assert.That(spec.DrWindowTicks, Is.EqualTo(100));
        Assert.That(spec.DrStageMultipliers,
            Is.EqualTo(new List<BigNum> { BigNum.FromParts(5, -1), BigNum.Zero }));
        Assert.That(spec.Stack.LevelFromStack, Is.True);
        Assert.That(spec.Stack.ExtendCapMultiplier, Is.EqualTo(BigNum.FromParts(15, -1)));
    }

    [Test]
    public void New_fields_absent_keep_model_defaults()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "x", "duration": { "type": "Instant" },
          "modifiers": [], "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [], "assetTags": [], "immunityTags": [], "chains": [] } ] }
        """;
        var spec = Load(json)[0];

        Assert.That(spec.RemoveOnApplyTags, Is.Empty);
        Assert.That(spec.ChanceToApply, Is.Null);
        Assert.That(spec.DurationPerLevel, Is.Null);
        Assert.That(spec.DurationScale, Is.Null);
        Assert.That(spec.DrCategory, Is.Null);
        Assert.That(spec.DrWindowTicks, Is.EqualTo(0));
        Assert.That(spec.DrStageMultipliers, Is.Empty);
        Assert.That(spec.Stack.LevelFromStack, Is.False);
        Assert.That(spec.Stack.ExtendCapMultiplier, Is.EqualTo(BigNum.FromParts(13, -1)));
    }

    // ---- Two-path equivalence: DR spec (G6) ----

    private const string DrJson = """
    { "schemaVersion": 1, "specs": [ {
      "name": "cc", "duration": { "type": "Duration", "ticks": 10, "periodTicks": 0 },
      "modifiers": [], "executions": [], "applicationConditions": [], "ongoingConditions": [],
      "grantedTags": [], "assetTags": [], "immunityTags": [], "chains": [],
      "drCategory": "effect.frost", "drWindowTicks": 100, "drStageMultipliers": [ "0.5", "0" ] } ] }
    """;

    [Test]
    public void Code_built_and_json_loaded_dr_spec_produce_identical_duration_stages()
    {
        var codeSpec = EffectTestKit.MinimalDuration("cc", ticks: 10);
        codeSpec.DrCategory = "effect.frost";
        codeSpec.DrWindowTicks = 100;
        codeSpec.DrStageMultipliers = new List<BigNum> { BigNum.FromParts(5, -1), BigNum.Zero };

        var codeKit = EffectTestKit.Create();
        codeKit.AddSpec(codeSpec);
        var codeSecondDuration = RunDrTwiceAndGetSecondDuration(codeKit);

        var jsonKit = EffectTestKit.Create();
        foreach (var spec in Load(DrJson)) jsonKit.AddSpec(spec);
        var jsonSecondDuration = RunDrTwiceAndGetSecondDuration(jsonKit);

        Assert.That(jsonSecondDuration, Is.EqualTo(codeSecondDuration));
        Assert.That(jsonSecondDuration, Is.EqualTo(5));
    }

    private static int RunDrTwiceAndGetSecondDuration(EffectTestKit kit)
    {
        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        for (var i = 0; i < 50 && kit.Defender.ActiveEffectCount > 0; i++) pipeline.Tick();

        pipeline.EnqueueApply(kit.SpecId("cc"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        return kit.Defender.ActiveEffects[0].RemainingTicks;
    }

    [Test]
    public void MaxLevel_missing_at_root_defaults_to_zero()
    {
        const string json = """
        { "schemaVersion": 1, "specs": [ {
          "name": "x", "duration": { "type": "Instant" },
          "modifiers": [], "executions": [], "applicationConditions": [], "ongoingConditions": [],
          "grantedTags": [], "assetTags": [], "immunityTags": [], "chains": [] } ] }
        """;
        Assert.That(Load(json)[0].MaxLevel, Is.EqualTo(0));
    }
}
