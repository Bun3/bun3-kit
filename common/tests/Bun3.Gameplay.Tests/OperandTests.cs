#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class OperandTests
{
    [Test]
    public void Constant_and_attribute_factories_populate_kind_and_fields()
    {
        var constant = Operand.Constant(50);
        Assert.That(constant.Kind, Is.EqualTo(OperandKind.Constant));
        Assert.That(constant.Value, Is.EqualTo((BigNum)50));

        var plain = Operand.Attribute(3);
        Assert.That(plain.Kind, Is.EqualTo(OperandKind.Attribute));
        Assert.That(plain.AttributeId, Is.EqualTo((ushort)3));
        Assert.That(plain.Value, Is.EqualTo(BigNum.One));   // 계수 기본 1

        var scaled = Operand.Attribute(3, BigNum.FromParts(3, -1));   // ×0.3
        Assert.That(scaled.Value, Is.EqualTo(BigNum.FromParts(3, -1)));
    }

    [Test]
    public void Operands_compare_by_value()
    {
        Assert.That(Operand.Constant(50), Is.EqualTo(Operand.Constant(50)));
        Assert.That(Operand.Constant(50), Is.Not.EqualTo(Operand.Attribute(3)));
        Assert.That(Operand.Attribute(3, 2), Is.Not.EqualTo(Operand.Attribute(3, 5)));
    }

    [Test]
    public void Default_policies_match_the_spec()
    {
        Assert.That(default(MaxIncreasePolicy), Is.EqualTo(MaxIncreasePolicy.Stay));
        Assert.That(default(MaxDecreasePolicy), Is.EqualTo(MaxDecreasePolicy.Follow));
    }

    [Test]
    public void SourceAttribute_factory_populates_kind_and_fields()
    {
        // 계수 기본 1
        var plain = Operand.SourceAttribute(3);
        Assert.That(plain.Kind, Is.EqualTo(OperandKind.SourceAttribute));
        Assert.That(plain.AttributeId, Is.EqualTo((ushort)3));
        Assert.That(plain.Value, Is.EqualTo(BigNum.One));

        // 명시적 계수
        var scaled = Operand.SourceAttribute(3, BigNum.FromParts(3, -1));   // ×0.3
        Assert.That(scaled.Kind, Is.EqualTo(OperandKind.SourceAttribute));
        Assert.That(scaled.AttributeId, Is.EqualTo((ushort)3));
        Assert.That(scaled.Value, Is.EqualTo(BigNum.FromParts(3, -1)));
    }

    [Test]
    public void SourceAttribute_not_equal_to_attribute_same_id_coeff()
    {
        // Kind 다름: SourceAttribute vs Attribute
        var source = Operand.SourceAttribute(3, 2);
        var target = Operand.Attribute(3, 2);
        Assert.That(source, Is.Not.EqualTo(target));
        Assert.That(source.GetHashCode(), Is.Not.EqualTo(target.GetHashCode()));
    }

    [Test]
    public void Equal_source_attributes_are_equal()
    {
        var a = Operand.SourceAttribute(3);
        var b = Operand.SourceAttribute(3);
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));

        var c = Operand.SourceAttribute(3, BigNum.FromParts(2, 0));
        var d = Operand.SourceAttribute(3, BigNum.FromParts(2, 0));
        Assert.That(c, Is.EqualTo(d));
        Assert.That(c.GetHashCode(), Is.EqualTo(d.GetHashCode()));
    }
}
