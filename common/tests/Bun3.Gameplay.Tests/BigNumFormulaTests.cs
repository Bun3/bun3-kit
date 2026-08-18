#nullable enable
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class BigNumFormulaTests
{
    private static BigNum N(string s)
    {
        Assert.That(BigNum.TryParse(s, out var v), Is.True, $"파싱 실패: {s}");
        return v;
    }

    [TestCase("x*x*3+10", "5", "85")]
    [TestCase("(x+1)/2", "9", "5")]
    [TestCase("x^3-2*x", "3", "21")]
    [TestCase("-x+100", "30", "70")]
    [TestCase("0.5*x", "8", "4")]
    public void Evaluates_expected_value(string formula, string x, string expected)
    {
        Assert.That(BigNumFormula.TryEvaluate(formula, N(x), out var result), Is.True);
        Assert.That(result, Is.EqualTo(N(expected)));
    }

    [TestCase("x^1.5")]
    [TestCase("x^x")]
    [TestCase("y+1")]
    [TestCase("1++2")]
    [TestCase("")]
    public void Rejects_invalid_formula(string formula)
    {
        Assert.That(BigNumFormula.TryEvaluate(formula, BigNum.One, out _), Is.False);
    }

    [Test]
    public void Division_by_zero_fails_at_evaluation_not_validation()
    {
        Assert.That(BigNumFormula.TryValidate("1/0", out var error), Is.True, error);
        Assert.That(BigNumFormula.TryEvaluate("1/0", BigNum.One, out _), Is.False);
    }

    [Test]
    public void TryValidate_accepts_well_formed_formula()
    {
        Assert.That(BigNumFormula.TryValidate("x*x*3+10", out var error), Is.True);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void TryValidate_rejects_malformed_formula_with_error()
    {
        Assert.That(BigNumFormula.TryValidate("x^x", out var error), Is.False);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }
}
