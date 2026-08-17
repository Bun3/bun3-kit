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
    // EffectTestKit의 속성 id 상수와 맞춘 이름 사전.
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
        // JSON에 없던 필드는 모델 기본값을 유지해야 한다.
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

    // ---- 두 경로 동등성: chill/frozen/poison을 코드로 구축한 카탈로그 vs JSON에서 로드한 카탈로그 ----

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
        for (var i = 0; i < 4; i++)   // 4번째 재적용에서 스택 초과 -> frozen
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
}
