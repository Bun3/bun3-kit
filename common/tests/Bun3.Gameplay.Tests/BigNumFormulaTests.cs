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

    // Golden values captured from the pre-compilation interpreter (commit 464404a) by running it
    // in a throwaway scratch project, one row per (formula, x). Exercises left-associative
    // subtraction/division chains, mixed precedence, doubled unary minus, nested parens, integer
    // power edge exponents, and decimal/scientific literals — exactly the shapes a swapped
    // operand or a repeated-squaring power bug would break.
    private static readonly (string Formula, int X, long Mantissa, int Exponent)[] GoldenValues =
    {
        ("x-1-1", 1, -1, 0), ("x-1-1", 2, 0, 0), ("x-1-1", 3, 1, 0), ("x-1-1", 7, 5, 0),
        ("10-x-2", 1, 7, 0), ("10-x-2", 2, 6, 0), ("10-x-2", 3, 5, 0), ("10-x-2", 7, 1, 0),
        ("x/2/2", 1, 25, -2), ("x/2/2", 2, 5, -1), ("x/2/2", 3, 75, -2), ("x/2/2", 7, 175, -2),
        ("100/x/2", 1, 5, 1), ("100/x/2", 2, 25, 0),
        ("100/x/2", 3, 1666666666666666665, -17), ("100/x/2", 7, 714285714285714285, -17),
        ("x*x*x - 3*x + 2", 1, 0, 0), ("x*x*x - 3*x + 2", 2, 4, 0),
        ("x*x*x - 3*x + 2", 3, 2, 1), ("x*x*x - 3*x + 2", 7, 324, 0),
        ("-x", 1, -1, 0), ("-x", 2, -2, 0), ("-x", 3, -3, 0), ("-x", 7, -7, 0),
        ("- -x", 1, 1, 0), ("- -x", 2, 2, 0), ("- -x", 3, 3, 0), ("- -x", 7, 7, 0),
        ("-x^2", 1, -1, 0), ("-x^2", 2, -4, 0), ("-x^2", 3, -9, 0), ("-x^2", 7, -49, 0),
        ("(x+1)*(x-1)", 1, 0, 0), ("(x+1)*(x-1)", 2, 3, 0),
        ("(x+1)*(x-1)", 3, 8, 0), ("(x+1)*(x-1)", 7, 48, 0),
        ("((x))", 1, 1, 0), ("((x))", 2, 2, 0), ("((x))", 3, 3, 0), ("((x))", 7, 7, 0),
        ("x^0", 1, 1, 0), ("x^0", 2, 1, 0), ("x^0", 3, 1, 0), ("x^0", 7, 1, 0),
        ("x^1", 1, 1, 0), ("x^1", 2, 2, 0), ("x^1", 3, 3, 0), ("x^1", 7, 7, 0),
        ("x^3", 1, 1, 0), ("x^3", 2, 8, 0), ("x^3", 3, 27, 0), ("x^3", 7, 343, 0),
        ("1.5*x + 0.25", 1, 175, -2), ("1.5*x + 0.25", 2, 325, -2),
        ("1.5*x + 0.25", 3, 475, -2), ("1.5*x + 0.25", 7, 1075, -2),
        ("1e3*x", 1, 1, 3), ("1e3*x", 2, 2, 3), ("1e3*x", 3, 3, 3), ("1e3*x", 7, 7, 3),
    };

    [Test]
    public void Compiled_path_matches_golden_values_from_pre_compilation_interpreter()
    {
        foreach (var (formula, x, expectedMantissa, expectedExponent) in GoldenValues)
        {
            Assert.That(BigNumFormula.TryCompile(formula, out var compiled, out var error), Is.True, $"compile failed: {formula} ({error})");
            Assert.That(compiled!.TryEvaluate(x, out var result), Is.True, $"{formula} @ x={x}");
            Assert.That(result.Mantissa, Is.EqualTo(expectedMantissa), $"{formula} @ x={x}: mantissa");
            Assert.That(result.Exponent, Is.EqualTo(expectedExponent), $"{formula} @ x={x}: exponent");
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
