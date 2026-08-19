#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AttributeRegistryTests
{
    private const ushort Hp = 1;
    private const ushort MaxHp = 2;
    private const ushort Mp = 3;
    private const ushort MaxMp = 4;

    [Test]
    public void Registration_order_does_not_matter_and_forward_references_are_allowed()
    {
        var forward = new AttributeRegistryBuilder();
        forward.Register(Hp, min: Operand.Constant(0), max: Operand.Attribute(MaxHp));
        forward.Register(MaxHp, min: Operand.Constant(1));

        var backward = new AttributeRegistryBuilder();
        backward.Register(MaxHp, min: Operand.Constant(1));
        backward.Register(Hp, min: Operand.Constant(0), max: Operand.Attribute(MaxHp));

        var a = forward.Build();
        var b = backward.Build();
        Assert.That(a.EvaluationOrder.ToArray(), Is.EqualTo(b.EvaluationOrder.ToArray()));
        Assert.That(a.GetClampDependents(MaxHp).ToArray(), Is.EqualTo(new[] { Hp }));
    }

    [Test]
    public void Evaluation_order_puts_referenced_attributes_first_with_id_tiebreak()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(Hp, max: Operand.Attribute(MaxHp));
        builder.Register(Mp, max: Operand.Attribute(MaxMp));
        builder.Register(MaxMp);
        builder.Register(MaxHp);
        var registry = builder.Build();

        var order = registry.EvaluationOrder.ToArray();
        Assert.That(Array.IndexOf(order, MaxHp), Is.LessThan(Array.IndexOf(order, Hp)));
        Assert.That(Array.IndexOf(order, MaxMp), Is.LessThan(Array.IndexOf(order, Mp)));
        // 독립 원소끼리는 id 오름차순
        Assert.That(order, Is.EqualTo(new ushort[] { MaxHp, MaxMp, Hp, Mp }).Or.EqualTo(new ushort[] { MaxMp, MaxHp, Hp, Mp }));
        Assert.That(order[0], Is.EqualTo(MaxHp));   // 동순위 타이브레이크 = id 오름차순이므로 2 < 4
    }

    [Test]
    public void Build_rejects_missing_reference_cycle_and_meaningless_policy()
    {
        var missing = new AttributeRegistryBuilder();
        missing.Register(Hp, max: Operand.Attribute(MaxHp));   // MaxHp 미등록
        Assert.Throws<InvalidOperationException>(() => missing.Build());

        var cyclic = new AttributeRegistryBuilder();
        cyclic.Register(Hp, max: Operand.Attribute(MaxHp));
        cyclic.Register(MaxHp, max: Operand.Attribute(Hp));
        Assert.Throws<InvalidOperationException>(() => cyclic.Build());

        var meaningless = new AttributeRegistryBuilder();
        meaningless.Register(Hp, max: Operand.Constant(100), onMaxIncrease: MaxIncreasePolicy.Follow);
        Assert.Throws<InvalidOperationException>(() => meaningless.Build());   // max가 속성 참조 아님

        var duplicated = new AttributeRegistryBuilder();
        duplicated.Register(Hp);
        Assert.Throws<InvalidOperationException>(() => duplicated.Register(Hp));

        var frozen = new AttributeRegistryBuilder();
        frozen.Register(Hp);
        frozen.Build();
        Assert.Throws<InvalidOperationException>(() => frozen.Register(Mp));
    }

    [Test]
    public void Build_rejects_source_attribute_operand_in_clamp_bounds()
    {
        var sourceMinBound = new AttributeRegistryBuilder();
        sourceMinBound.Register(Hp, min: Operand.SourceAttribute(MaxHp));
        Assert.Throws<InvalidOperationException>(() => sourceMinBound.Build(),
            "min bound with SourceAttribute should be rejected");

        var sourceMaxBound = new AttributeRegistryBuilder();
        sourceMaxBound.Register(Hp, max: Operand.SourceAttribute(MaxHp));
        Assert.Throws<InvalidOperationException>(() => sourceMaxBound.Build(),
            "max bound with SourceAttribute should be rejected");
    }

    [Test]
    public void Evaluation_order_is_level_based_not_greedy_min()
    {
        // 레벨별 Kahn과 greedy-min의 차이를 구분:
        // 속성 1 (독립), 속성 2 (1 참조), 속성 5 (독립)
        // 레벨별: [1, 5, 2] (레벨 0: 1,5; 레벨 1: 2)
        // greedy-min: [1, 2, 5] (매 번 최솟값 선택)
        var builder = new AttributeRegistryBuilder();
        builder.Register(1);           // 독립
        builder.Register(2, max: Operand.Attribute(1));  // 1 참조
        builder.Register(5);           // 독립
        var registry = builder.Build();

        Assert.That(registry.EvaluationOrder.ToArray(), Is.EqualTo(new ushort[] { 1, 5, 2 }));
    }
}
