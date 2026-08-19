#nullable enable
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class BigNumFormulaTests
{
    private static BigNum N(string s)
    {
        Assert.That(BigNum.TryParse(s, out var v), Is.True, $"parse failed: {s}");
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

    [Test]
    public void Compiled_path_matches_interpreted_path_bit_for_bit()
    {
        string[] formulas =
        {
            "1", "x", "-x", "x+1", "1-x", "x*x", "x/2", "2/x", "x^3", "-x^2",
            "(x+1)*(x-1)", "x*x*x - 3*x + 2", "1.5*x + 0.25", "1e3*x", "x^0", "((x))",
        };

        foreach (var formula in formulas)
        {
            Assert.That(BigNumFormula.TryCompile(formula, out var compiled, out var error), Is.True, $"compile failed: {formula} ({error})");
            for (var level = 1; level <= 20; level++)
            {
                var interpretedOk = BigNumFormula.TryEvaluate(formula, level, out var interpreted);
                var compiledOk = compiled!.TryEvaluate(level, out var fromCompiled);
                Assert.That(compiledOk, Is.EqualTo(interpretedOk), $"{formula} @ {level}");
                if (interpretedOk)
                {
                    Assert.That(fromCompiled.Mantissa, Is.EqualTo(interpreted.Mantissa), $"{formula} @ {level}");
                    Assert.That(fromCompiled.Exponent, Is.EqualTo(interpreted.Exponent), $"{formula} @ {level}");
                }
            }
        }
    }

    [Test]
    public void Compile_rejects_invalid_grammar()
    {
        Assert.That(BigNumFormula.TryCompile("x +", out var compiled, out var error), Is.False);
        Assert.That(compiled, Is.Null);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void Compiled_division_by_zero_fails_at_evaluation_not_compilation()
    {
        Assert.That(BigNumFormula.TryCompile("1/(x-5)", out var compiled, out _), Is.True);
        Assert.That(compiled!.TryEvaluate(4, out _), Is.True);
        Assert.That(compiled.TryEvaluate(5, out _), Is.False);
    }
}
