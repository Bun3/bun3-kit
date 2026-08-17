using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectCatalogBuilderTests
{
    private sealed class FixedMagnitude : IMagnitudeCalc
    {
        public BigNum Calculate(in MagnitudeContext ctx) => 7;
    }

    // 규칙 1: 이름 중복 금지·비어 있을 수 없음.

    [Test]
    public void Build_rejects_empty_name()
    {
        var builder = new EffectCatalogBuilder();
        builder.Add(EffectTestKit.MinimalInstant(""));
        Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
    }

    [Test]
    public void Build_rejects_duplicate_name()
    {
        var builder = new EffectCatalogBuilder();
        builder.Add(EffectTestKit.MinimalInstant("dup"));
        builder.Add(EffectTestKit.MinimalInstant("dup"));
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("dup"));
    }

    // 규칙 2: Instant는 Duration 전용 필드가 비어 있어야 한다.

    [Test]
    public void Build_rejects_instant_with_duration_only_fields()
    {
        var spec = EffectTestKit.MinimalInstant("bad");
        spec.GrantedTags.Add("state.dead");
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("bad"));
    }

    // 규칙 3: Duration/Infinite의 DurationTicks 제약.

    [Test]
    public void Build_rejects_duration_type_with_nonpositive_ticks()
    {
        var builder = new EffectCatalogBuilder();
        builder.Add(EffectTestKit.MinimalDuration("d", 0));
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("d"));
    }

    [Test]
    public void Build_rejects_infinite_type_with_nonzero_ticks()
    {
        var spec = EffectTestKit.MinimalInfinite("i");
        spec.DurationTicks = 5;
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    // 규칙 4: Executions는 Instant이거나 PeriodTicks > 0일 때만.

    [Test]
    public void Build_rejects_executions_on_duration_without_period()
    {
        var spec = EffectTestKit.MinimalDuration("d", 10);
        spec.Executions.Add(new ExecutionDef { CalcTag = "calc.execution.dmg" });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("d"));
    }

    // 규칙 5: 스택 없음(MaxStack == 0)인데 스택 전용 정책 사용.

    [Test]
    public void Build_rejects_add_stack_reapply_without_max_stack()
    {
        var spec = EffectTestKit.MinimalDuration("s", 10);
        spec.Stack.OnReapply = StackReapply.AddStack;
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("s"));
    }

    [Test]
    public void Build_rejects_apply_effect_overflow_without_max_stack()
    {
        var spec = EffectTestKit.MinimalDuration("s2", 10);
        spec.Stack.OnOverflow = StackOverflow.ApplyEffect;
        spec.Stack.OverflowEffectName = "s2";
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("s2"));
    }

    // 규칙: Instant/주기 실행 스펙의 수정자는 Add만 허용한다.

    [Test]
    public void Build_rejects_instant_modifier_with_multiply_op()
    {
        var spec = EffectTestKit.MinimalInstant("mul");
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Multiply,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(-3, -1)) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("mul"));
    }

    [Test]
    public void Build_rejects_periodic_duration_modifier_with_override_op()
    {
        var spec = EffectTestKit.MinimalDuration("periodic", 100);
        spec.PeriodTicks = 10;
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Override,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(0) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("periodic"));
    }

    [Test]
    public void Build_allows_multiply_op_on_non_periodic_duration_modifier()
    {
        var spec = EffectTestKit.MinimalDuration("buff", 100);
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Multiply,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, -1)) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        Assert.DoesNotThrow(() => EffectTestKit.BuildCatalog(builder));
    }

    // 규칙 6: 태그·CalcTag·SelectorTag는 카탈로그·SeamRegistry에서 해석되어야 한다.

    [Test]
    public void Build_rejects_unresolved_granted_tag()
    {
        var spec = EffectTestKit.MinimalDuration("d", 5);
        spec.GrantedTags.Add("no.such.tag");
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("d"));
    }

    [Test]
    public void Build_rejects_unresolved_magnitude_calc_tag()
    {
        // "calc.magnitude.x"는 공유 카탈로그엔 있지만 EffectTestKit.BuildCatalog의 SeamRegistry는 비어 있다.
        var spec = EffectTestKit.MinimalInstant("i");
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x" },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    [Test]
    public void Build_rejects_unresolved_chain_selector_tag()
    {
        var a = EffectTestKit.MinimalInstant("a");
        var edge = EffectTestKit.Edge(ChainTrigger.OnCompleteNormal, "a");
        edge.SelectorTag = "selector.team";   // 카탈로그엔 있지만 SeamRegistry엔 미등록
        a.Chains.Add(edge);
        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("a"));
    }

    // 규칙 7: Operand의 속성 참조는 등록되어 있어야 하고, OngoingConditions는 SourceAttribute를 가질 수 없다.

    [Test]
    public void Build_rejects_operand_referencing_unregistered_attribute()
    {
        var spec = EffectTestKit.MinimalInstant("i");
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Attribute(999) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    [Test]
    public void Build_rejects_source_attribute_operand_in_ongoing_conditions()
    {
        var spec = EffectTestKit.MinimalDuration("d", 5);
        spec.OngoingConditions.Add(new ConditionDef
        {
            Left = Operand.SourceAttribute(EffectTestKit.Hp),
            Op = ComparisonOp.Greater,
            Right = Operand.Constant(0),
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("d"));
    }

    // 규칙 8: 체인·Overflow의 EffectName은 카탈로그 안에서 해석되어야 한다.

    [Test]
    public void Build_rejects_unresolved_chain_effect_name()
    {
        var a = EffectTestKit.MinimalInstant("a");
        a.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "missing"));
        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("a"));
    }

    // 규칙 9: MagnitudeDef는 CalcTag XOR Base. PerLevel은 Base가 있을 때만.

    [Test]
    public void Build_rejects_magnitude_with_both_calctag_and_base()
    {
        var spec = EffectTestKit.MinimalInstant("i");
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x", Base = Operand.Constant(1) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    [Test]
    public void Build_rejects_magnitude_with_neither_calctag_nor_base()
    {
        var spec = EffectTestKit.MinimalInstant("i");
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef(),
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    [Test]
    public void Build_rejects_perlevel_without_base()
    {
        var spec = EffectTestKit.MinimalInstant("i");
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x", PerLevel = Operand.Constant(1) },
        });
        var builder = new EffectCatalogBuilder();
        builder.Add(spec);
        var ex = Assert.Throws<InvalidOperationException>(() => EffectTestKit.BuildCatalog(builder));
        Assert.That(ex!.Message, Does.Contain("i"));
    }

    // 규칙 10: 체인 순환은 예외가 아니라 경고다.

    [Test]
    public void Application_only_cycle_is_a_high_warning_not_an_error()
    {
        var a = EffectTestKit.MinimalInstant("a");
        a.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "b"));
        var b = EffectTestKit.MinimalInstant("b");
        b.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "a"));
        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        builder.Add(b);
        var catalog = EffectTestKit.BuildCatalog(builder);
        Assert.That(catalog.BuildWarnings, Has.Some.Contains("high"));
    }

    [Test]
    public void Cycle_through_duration_spec_is_a_low_warning()
    {
        var a = EffectTestKit.MinimalDuration("a", 10);
        a.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "b"));
        var b = EffectTestKit.MinimalInstant("b");
        b.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "a"));
        var builder = new EffectCatalogBuilder();
        builder.Add(a);
        builder.Add(b);
        var catalog = EffectTestKit.BuildCatalog(builder);
        Assert.That(catalog.BuildWarnings, Has.Some.Contains("low"));
    }

    // 그 외: 이름 조회, happy path 컴파일 결과 확인.

    [Test]
    public void GetRequiredId_and_TryGetId_resolve_registered_names()
    {
        var builder = new EffectCatalogBuilder();
        builder.Add(EffectTestKit.MinimalInstant("a"));
        var catalog = EffectTestKit.BuildCatalog(builder);

        Assert.That(catalog.GetRequiredId("a"), Is.EqualTo(0));
        Assert.That(catalog.TryGetId("a", out var id), Is.True);
        Assert.That(id, Is.EqualTo(0));
        Assert.That(catalog.TryGetId("missing", out _), Is.False);
        Assert.Throws<KeyNotFoundException>(() => catalog.GetRequiredId("missing"));
    }

    [Test]
    public void Happy_path_compiles_instant_modifier_and_duration_with_granted_tags_and_chain()
    {
        var tags = EffectTestKit.LoadCatalog();
        var seamBuilder = new SeamRegistryBuilder();
        var magCalc = new FixedMagnitude();
        seamBuilder.RegisterMagnitudeCalc(tags.GetRequired("calc.magnitude.x"), magCalc);
        var seams = seamBuilder.Build(tags);
        var attributes = EffectTestKit.BuildAttributeRegistry();

        var bolt = EffectTestKit.MinimalInstant("bolt");
        bolt.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x" },
        });

        var hasted = EffectTestKit.MinimalDuration("hasted", 100);
        hasted.GrantedTags.Add("state.hasted");
        hasted.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "bolt"));

        var builder = new EffectCatalogBuilder();
        builder.Add(bolt);
        builder.Add(hasted);
        var catalog = builder.Build(tags, seams, attributes);

        Assert.That(catalog.Count, Is.EqualTo(2));

        var boltId = catalog.GetRequiredId("bolt");
        var boltSpec = catalog.GetSpec(boltId);
        Assert.That(boltSpec.Modifiers[0].Calc, Is.SameAs(magCalc));
        Assert.That(boltSpec.Modifiers[0].AttributeId, Is.EqualTo(EffectTestKit.Hp));

        var hastedId = catalog.GetRequiredId("hasted");
        var hastedSpec = catalog.GetSpec(hastedId);
        Assert.That(hastedSpec.GrantedTags[0], Is.EqualTo(tags.GetRequired("state.hasted")));
        Assert.That(hastedSpec.Chains[0].EffectId, Is.EqualTo(boltId));
        Assert.That(catalog.BuildWarnings, Is.Empty);
    }
}
