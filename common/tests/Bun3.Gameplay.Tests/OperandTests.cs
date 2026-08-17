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
}
