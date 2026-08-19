using System;
using System.Numerics;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class BigNumMulDivTests
{
    [Test]
    public void Multiply_small_integers_exact()
    {
        Assert.That((BigNum)6 * 7, Is.EqualTo((BigNum)42));
        Assert.That((BigNum)(-6) * 7, Is.EqualTo((BigNum)(-42)));
        Assert.That((BigNum)123_456_789L * 1_000_000_000L,
            Is.EqualTo((BigNum)123_456_789_000_000_000L));
        Assert.That(BigNum.Zero * 12345, Is.EqualTo(BigNum.Zero));
    }

    [Test]
    public void Multiply_huge_matches_BigInteger_leading_digits()
    {
        var a = (BigNum)140_000_000_000_000L;
        var product = a * a;
        Assert.That(product, Is.EqualTo(BigNum.FromParts(196, 26)));   // 1.96e28
    }

    [Test]
    public void Multiply_retains_18_significant_digits()
    {
        // Product of two 18-digit numbers: leading 18-19 digits must match the BigInteger oracle
        long m1 = 123_456_789_012_345_678L;
        long m2 = 987_654_321_098_765_432L;
        var product = (BigNum)m1 * m2;

        var oracle = (BigInteger)m1 * m2;                 // 121932631137021795... (36 digits)
        var oracleStr = oracle.ToString();
        // Canonicalization may shift trailing zeros into the exponent, so check the whole mantissa as an oracle prefix
        var resultDigits = Math.Abs(product.Mantissa).ToString();
        Assert.That(oracleStr.StartsWith(resultDigits), Is.True,
            $"oracle={oracleStr} result={product.Mantissa}e{product.Exponent}");
        Assert.That(resultDigits.Length, Is.GreaterThanOrEqualTo(17), "significant digits retained");
    }

    [Test]
    public void Percent_scaling_pattern_works_at_idle_scale()
    {
        var damage = BigNum.FromParts(37, 28);            // 3.7e29
        var multiplied = damage * BigNum.FromParts(15, -1);   // ×1.5
        Assert.That(multiplied, Is.EqualTo(BigNum.FromParts(555, 27)));   // 5.55e29
    }

    [Test]
    public void Divide_exact_and_truncating()
    {
        Assert.That((BigNum)84 / 2, Is.EqualTo((BigNum)42));
        Assert.That((BigNum)1 / 4, Is.EqualTo(BigNum.FromParts(25, -2)));    // 0.25
        Assert.That((BigNum)(-84) / 2, Is.EqualTo((BigNum)(-42)));

        // 1/3 = 0.333... truncated at 18-19 digits
        var third = (BigNum)1 / 3;
        Assert.That(third.Sign, Is.EqualTo(1));
        Assert.That(third < BigNum.FromParts(334, -3) && third > BigNum.FromParts(333, -3),
            Is.True, $"1/3 = {third.Mantissa}e{third.Exponent}");
    }

    [Test]
    public void Divide_huge_by_tiny_and_vice_versa()
    {
        var huge = BigNum.FromParts(5, 40);
        Assert.That(huge / BigNum.FromParts(2, -3), Is.EqualTo(BigNum.FromParts(25, 42)));
        Assert.That(BigNum.FromParts(2, -3) / huge, Is.EqualTo(BigNum.FromParts(4, -44)));
    }

    [Test]
    public void Divide_by_zero_throws()
    {
        Assert.Throws<DivideByZeroException>(() => _ = (BigNum)1 / BigNum.Zero);
    }

    [Test]
    public void Exponent_overflow_on_multiply_throws()
    {
        var big = BigNum.FromParts(1, BigNum.MaxExponent - 1);
        Assert.Throws<BigNumOverflowException>(() => _ = big * big);
    }

    [Test]
    public void Exponent_underflow_on_divide_returns_zero()
    {
        var tiny = BigNum.FromParts(1, -BigNum.MaxExponent + 1);
        var huge = BigNum.FromParts(1, BigNum.MaxExponent - 1);
        Assert.That(tiny / huge, Is.EqualTo(BigNum.Zero));
    }

    [Test]
    public void Divide_preserves_significant_digits_in_mid_range()
    {
        // Intermediate in [2^64, 1e35): must not shave more digits than necessary
        Assert.That((BigNum)77 / 3, Is.EqualTo(BigNum.FromParts(2_566_666_666_666_666_666L, -17)));
        Assert.That((BigNum)92 / 3, Is.EqualTo(BigNum.FromParts(3_066_666_666_666_666_666L, -17)));
    }

    [Test]
    public void Multiply_preserves_significant_digits_in_mid_range()
    {
        // 9999999999^2 = 99999999980000000001, truncated at 19 digits
        Assert.That((BigNum)9_999_999_999L * 9_999_999_999L,
            Is.EqualTo(BigNum.FromParts(9_999_999_998L, 10)));

        // Full-precision mantissa times a small multiplier ("value * 1.05" pattern)
        var third = (BigNum)1 / 3;                            // 3.33...e-1 (18 digits)
        var scaled = third * BigNum.FromParts(105, -1);       // × 10.5
        Assert.That(scaled, Is.EqualTo(BigNum.FromParts(3_499_999_999_999_999_996L, -18)));
    }

    [Test]
    public void Divide_matches_BigInteger_oracle_prefix()
    {
        long[] numerators = { 77, 92, 1_000_000_007, 123_456_789_012_345_680, long.MaxValue };
        long[] denominators = { 3, 999_999_937, 987_654_321 };
        foreach (var n in numerators)
        foreach (var d in denominators)
        {
            var q = (BigNum)n / d;
            var oracle = (BigInteger)n * BigInteger.Pow(10, 30) / d;
            var oracleStr = oracle.ToString();
            var mantissaStr = Math.Abs(q.Mantissa).ToString();
            Assert.That(oracleStr.StartsWith(mantissaStr), Is.True,
                $"{n}/{d}: got {q.Mantissa}e{q.Exponent}");
            Assert.That(mantissaStr.Length, Is.GreaterThanOrEqualTo(17), $"{n}/{d} significant digits retained");
        }
    }

    [Test]
    public void Divide_small_by_large_mantissa_does_not_collapse_to_zero()
    {
        // Regression: collapsed to zero when the denominator mantissa exceeded numerator*10^18
        var q = (BigNum)1 / long.MaxValue;   // ≈ 1.0842e-19
        Assert.That(q.IsZero, Is.False);
        Assert.That(q > BigNum.FromParts(1084, -22) && q < BigNum.FromParts(1085, -22), Is.True,
            $"got {q.Mantissa}e{q.Exponent}");
    }
}
