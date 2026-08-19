#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectInstantTests
{
    [Test]
    public void Instant_modifier_permanently_changes_base()
    {
        var kit = EffectTestKit.Create();                       // 카탈로그·레지스트리·타깃 2개(공/수) 조립
        var damage = EffectTestKit.MinimalInstant("hit");
        damage.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-30) },
        });
        kit.AddSpec(damage);
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);
        pipeline.EnqueueApply(kit.SpecId("hit"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)70));
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);   // Instant는 인스턴스 없음
    }

    [Test]
    public void Execution_calc_reads_inputs_and_writes_through_clamp()
    {
        var kit = EffectTestKit.Create();
        kit.RegisterExecutionCalc("calc.execution.dmg", new EffectTestKit.SubtractHpCalc()); // Input(0)만큼 Hp 감소
        var spell = EffectTestKit.MinimalInstant("spell");
        spell.Executions.Add(new ExecutionDef
        {
            CalcTag = "calc.execution.dmg",
            Inputs = { Operand.Attribute(EffectTestKit.MaxHp, BigNum.FromParts(5, -1)) }, // 최대체력의 50%
        });
        kit.AddSpec(spell);
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 200);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 150);
        pipeline.EnqueueApply(kit.SpecId("spell"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Hp), Is.EqualTo((BigNum)50));
    }

    [Test]
    public void Application_condition_and_immunity_block_application()
    {
        var kit = EffectTestKit.Create();
        var gated = EffectTestKit.MinimalInstant("gated");
        gated.ApplicationConditions.Add(new ConditionDef
        {
            Left = Operand.Attribute(EffectTestKit.Hp),
            Op = ComparisonOp.Less,
            Right = Operand.Attribute(EffectTestKit.MaxHp, BigNum.FromParts(3, -1)),
        });
        gated.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-10) },
        });
        kit.AddSpec(gated);

        var ward = EffectTestKit.MinimalInfinite("ward");
        ward.ImmunityTags.Add("effect.fire");
        kit.AddSpec(ward);
        var fireball = EffectTestKit.MinimalInstant("fireball");
        fireball.AssetTags.Add("effect.fire.bolt");
        fireball.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-25) },
        });
        kit.AddSpec(fireball);
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 90);

        pipeline.EnqueueApply(kit.SpecId("gated"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)90)); // Hp<30% 아님

        pipeline.EnqueueApply(kit.SpecId("ward"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("fireball"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)90)); // 면역 차단
    }

    [Test]
    public void Source_attribute_modifier_scales_with_caster_stat()
    {
        var kit = EffectTestKit.Create();
        var slash = EffectTestKit.MinimalInstant("slash");
        slash.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.SourceAttribute(EffectTestKit.Attack, BigNum.FromParts(-12, -1)) },
        });
        kit.AddSpec(slash);
        var pipeline = kit.BuildPipeline();

        kit.Attacker.Attributes.SetBase(EffectTestKit.Attack, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 200);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 150);

        pipeline.EnqueueApply(kit.SpecId("slash"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // 공격력 100 × -1.2 = -120 → Hp 150 - 120 = 30.
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)30));
    }

    [Test]
    public void Magnitude_calc_reads_target_and_source_tags_via_seam_context()
    {
        var kit = EffectTestKit.Create();
        var frozenTag = kit.Tag("state.frozen");
        var hastedTag = kit.Tag("state.hasted");
        kit.RegisterMagnitudeCalc("calc.magnitude.x", new TagGatedMagnitudeCalc(frozenTag, -10, checkSource: false));
        kit.RegisterMagnitudeCalc("calc.magnitude.y", new TagGatedMagnitudeCalc(hastedTag, -10, checkSource: true));

        var frost = EffectTestKit.MinimalInfinite("frost");
        frost.GrantedTags.Add("state.frozen");
        kit.AddSpec(frost);

        var haste = EffectTestKit.MinimalInfinite("haste");
        haste.GrantedTags.Add("state.hasted");
        kit.AddSpec(haste);

        var hitTarget = EffectTestKit.MinimalInstant("hitTarget");
        hitTarget.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x" },
        });
        kit.AddSpec(hitTarget);

        var hitSource = EffectTestKit.MinimalInstant("hitSource");
        hitSource.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.y" },
        });
        kit.AddSpec(hitSource);

        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 200);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 200);

        // TargetHasTag: 대상에 frozen 없음 → 배율 없이 -10.
        pipeline.EnqueueApply(kit.SpecId("hitTarget"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)190));

        // 대상에 frozen 부여 후 → 2배 -20.
        pipeline.EnqueueApply(kit.SpecId("frost"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("hitTarget"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)170));

        // SourceHasTag: 시전자(Attacker)에 hasted 없음 → 배율 없이 -10.
        pipeline.EnqueueApply(kit.SpecId("hitSource"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)160));

        // 시전자에 hasted 부여 후 → 2배 -20.
        pipeline.EnqueueApply(kit.SpecId("haste"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("hitSource"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)140));
    }

    /// <summary>MagnitudeContext.TargetHasTag/SourceHasTag 회귀 테스트 전용 계산 — 지정한 쪽(대상 또는
    /// 시전자)이 태그를 보유하면 크기를 2배로 반환합니다.</summary>
    private sealed class TagGatedMagnitudeCalc : IMagnitudeCalc
    {
        private readonly GameplayTag _tag;
        private readonly BigNum _amount;
        private readonly bool _checkSource;

        public TagGatedMagnitudeCalc(GameplayTag tag, BigNum amount, bool checkSource)
        {
            _tag = tag;
            _amount = amount;
            _checkSource = checkSource;
        }

        public BigNum Calculate(in MagnitudeContext ctx)
        {
            var hasTag = _checkSource ? ctx.SourceHasTag(_tag) : ctx.TargetHasTag(_tag);
            return hasTag ? _amount * 2 : _amount;
        }
    }
}
