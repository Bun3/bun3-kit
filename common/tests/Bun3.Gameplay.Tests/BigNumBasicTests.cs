using System;
using System.Globalization;
using System.Numerics;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class BigNumBasicTests
{
    [Test]
    public void Canonical_form_is_unique()
    {
        // 같은 값의 서로 다른 표현이 같은 비트로 정규화된다
        var a = BigNum.FromParts(5, 0);
        var b = BigNum.FromParts(500, -2);
        var c = BigNum.FromParts(5_000_000, -6);
        Assert.That(a, Is.EqualTo(b));
        Assert.That(b, Is.EqualTo(c));
        Assert.That(a.Mantissa, Is.EqualTo(5));
        Assert.That(a.Exponent, Is.EqualTo(0));
        Assert.That(b.Mantissa, Is.EqualTo(5));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Zero_is_canonical()
    {
        Assert.That(BigNum.FromParts(0, 999).IsZero, Is.True);
        Assert.That(BigNum.FromParts(0, 999), Is.EqualTo(BigNum.Zero));
        Assert.That(BigNum.Zero.Exponent, Is.EqualTo(0));
        Assert.That(BigNum.Zero.Sign, Is.EqualTo(0));
    }

    [Test]
    public void Long_conversion_is_exact_to_long_max()
    {
        // 922경(long.MaxValue)까지 정수 정확 — 스펙 §6
        var max = (BigNum)long.MaxValue;
        Assert.That(max.Sign, Is.EqualTo(1));
        Assert.That(max, Is.EqualTo(BigNum.FromParts(long.MaxValue, 0)));

        var min = (BigNum)(long.MinValue + 1);
        Assert.That(min.Sign, Is.EqualTo(-1));
    }

    [Test]
    public void Long_min_value_is_rejected_instead_of_truncated()
    {
        Assert.That(() => _ = (BigNum)long.MinValue,
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => _ = BigNum.FromParts(long.MinValue, 17),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Published_extrema_are_exact_and_symmetric()
    {
        Assert.That(BigNum.MaxValue.Mantissa, Is.EqualTo(long.MaxValue));
        Assert.That(BigNum.MaxValue.Exponent, Is.EqualTo(BigNum.MaxExponent));
        Assert.That(BigNum.MinValue.Mantissa, Is.EqualTo(-long.MaxValue));
        Assert.That(BigNum.MinValue.Exponent, Is.EqualTo(BigNum.MaxExponent));
        Assert.That(-BigNum.MaxValue, Is.EqualTo(BigNum.MinValue));
        Assert.That(-BigNum.MinValue, Is.EqualTo(BigNum.MaxValue));
    }

    [Test]
    public void Addition_small_integers_exact()
    {
        Assert.That((BigNum)3 + 5, Is.EqualTo((BigNum)8));
        Assert.That((BigNum)1_000_000_000_000L + 1, Is.EqualTo((BigNum)1_000_000_000_001L));
        Assert.That((BigNum)7 + (-7), Is.EqualTo(BigNum.Zero));
    }

    [Test]
    public void Addition_at_long_scale_stays_exact()
    {
        // 9e18 + 2e17 = 9.2e18 — 유효 자릿수 안에서 정확
        var a = (BigNum)9_000_000_000_000_000_000L;
        var b = (BigNum)200_000_000_000_000_000L;
        Assert.That(a + b, Is.EqualTo((BigNum)9_200_000_000_000_000_000L));
    }

    [Test]
    public void Addition_beyond_significant_digits_drops_small_term()
    {
        // 1e30 + 1 = 1e30 — 방치형 표준 타협 (스펙 §6)
        var big = BigNum.FromParts(1, 30);
        Assert.That(big + 1, Is.EqualTo(big));
    }

    [TestCase(19, 999_999_999_999_999_999L, 1)]
    [TestCase(20, 999_999_999_999_999_999L, 2)]
    [TestCase(100, 999_999_999_999_999_999L, 82)]
    public void Opposite_sign_far_gap_propagates_borrow(
        int exponent, long expectedMantissa, int expectedExponent)
    {
        var actual = BigNum.FromParts(1, exponent) + (BigNum)(-1);
        Assert.That(actual, Is.EqualTo(BigNum.FromParts(expectedMantissa, expectedExponent)));
    }

    [TestCase(-1L, 1L, 19, 999_999_999_999_999_999L, 1)]
    [TestCase(-1L, 1L, 100, 999_999_999_999_999_999L, 82)]
    [TestCase(1L, -1L, 20, -999_999_999_999_999_999L, 2)]
    public void Opposite_sign_far_gap_with_larger_right_propagates_borrow(
        long smallerMantissa, long largerMantissa, int largerExponent,
        long expectedMantissa, int expectedExponent)
    {
        var actual = (BigNum)smallerMantissa
                     + BigNum.FromParts(largerMantissa, largerExponent);
        Assert.That(actual, Is.EqualTo(BigNum.FromParts(expectedMantissa, expectedExponent)));
    }

    [Test]
    public void Opposite_sign_far_gap_handles_canonical_nineteen_digit_mantissa_window()
    {
        var actual = BigNum.FromParts(9_223_372_036_854_775_807L, 20) + (BigNum)(-1);
        Assert.That(actual,
            Is.EqualTo(BigNum.FromParts(9_223_372_036_854_775_806L, 20)));
    }

    [Test]
    public void Opposite_sign_far_gap_handles_nineteen_digit_window_over_long_max()
    {
        var actual = BigNum.FromParts(999, 20) + (BigNum)(-1);
        Assert.That(actual,
            Is.EqualTo(BigNum.FromParts(998_999_999_999_999_999L, 5)));
    }

    [Test]
    public void Opposite_sign_far_gap_is_sign_symmetric()
    {
        var positive = BigNum.FromParts(1, 20) + (BigNum)(-1);
        var negative = BigNum.FromParts(-1, 20) + (BigNum)1;
        Assert.That(negative, Is.EqualTo(-positive));
    }

    [Test]
    public void Opposite_sign_far_gap_handles_max_exponent()
    {
        var actual = BigNum.FromParts(1, BigNum.MaxExponent) + (BigNum)(-1);
        Assert.That(actual, Is.EqualTo(BigNum.FromParts(
            999_999_999_999_999_999L, BigNum.MaxExponent - 18)));
    }

    [Test]
    public void Same_sign_far_gap_still_ignores_out_of_precision_operand()
    {
        Assert.That(
            BigNum.FromParts(1, 20) + (BigNum)1,
            Is.EqualTo(BigNum.FromParts(1, 20)));
    }

    [Test]
    public void Subtraction_cancellation_is_exact_within_window()
    {
        var a = BigNum.FromParts(1, 18);                 // 1e18
        var b = (BigNum)999_999_999_999_999_999L;        // 1e18 - 1
        Assert.That(a - b, Is.EqualTo((BigNum)1));
    }

    [Test]
    public void Comparison_orders_by_value()
    {
        Assert.That((BigNum)5 < 6, Is.True);
        Assert.That(BigNum.FromParts(1, 30) > BigNum.FromParts(999, 27), Is.True);   // 1e30 > 9.99e29
        Assert.That((BigNum)(-5) < 3, Is.True);
        Assert.That(BigNum.FromParts(-1, 30) < BigNum.FromParts(-999, 27), Is.True); // -1e30 < -9.99e29
        Assert.That((BigNum)7 <= 7 && (BigNum)7 >= 7, Is.True);
    }

    [Test]
    public void Exponent_overflow_throws_underflow_clamps_to_zero()
    {
        Assert.Throws<BigNumOverflowException>(() => BigNum.FromParts(1, BigNum.MaxExponent + 1));
        Assert.That(BigNum.FromParts(1, -BigNum.MaxExponent - 1), Is.EqualTo(BigNum.Zero));
    }

    [Test]
    public void Addition_is_exact_up_to_long_range()
    {
        // 실제 오버플로 전에는 절사하지 않는다 (최종 리뷰 Important 회귀 가드)
        Assert.That((BigNum)9_000_000_000_000_000_000L + 1,
            Is.EqualTo((BigNum)9_000_000_000_000_000_001L));
        Assert.That((BigNum)5_000_000_000_000_000_000L + 1,
            Is.EqualTo((BigNum)5_000_000_000_000_000_001L));

        // exact intermediate를 만든 뒤 한 번만 축약한다.
        Assert.That((BigNum)long.MaxValue + long.MaxValue,
            Is.EqualTo(BigNum.FromParts(1_844_674_407_370_955_161L, 1)));
    }

    [Test]
    public void Addition_reduces_valid_negative_long_min_intermediate()
    {
        var actual = (BigNum)(-long.MaxValue) + (BigNum)(-1);
        Assert.That(actual, Is.EqualTo(BigNum.FromParts(-92_233_720_368_547_758L, 2)));
    }

    [Test]
    public void Addition_reduces_exact_sum_once_after_carry()
    {
        var actual = (BigNum)long.MaxValue + (BigNum)long.MaxValue;
        Assert.That(actual, Is.EqualTo(BigNum.FromParts(1_844_674_407_370_955_161L, 1)));
    }

    [Test]
    public void Addition_preserves_exact_near_cancellation()
    {
        var actual = BigNum.FromParts(-922_337_203_685_477_581L, -2)
                     + BigNum.FromParts(3, -3);
        Assert.That(actual, Is.EqualTo(BigNum.FromParts(-long.MaxValue, -3)));
    }

    [Test]
    public void Comparison_is_total_at_extrema_and_opposite_signs()
    {
        Assert.That(BigNum.MaxValue.CompareTo(BigNum.MinValue), Is.GreaterThan(0));
        Assert.That(BigNum.MinValue.CompareTo(BigNum.MaxValue), Is.LessThan(0));
        Assert.That(BigNum.MaxValue > BigNum.MinValue, Is.True);
        Assert.That(((BigNum)(-1)).CompareTo((BigNum)long.MaxValue), Is.LessThan(0));
    }

    [Test]
    public void Hash_code_uses_fixed_fnv1a_golden()
    {
        Assert.That(BigNum.FromParts(12_345, 6).GetHashCode(), Is.EqualTo(930_490_798));
        Assert.That(BigNum.MinValue.GetHashCode(), Is.EqualTo(1_520_456_044));
    }

    [Test]
    public void ToString_uses_invariant_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        customCulture.NumberFormat.NegativeSign = "~";
        try
        {
            CultureInfo.CurrentCulture = customCulture;
            Assert.That(BigNum.FromParts(-123, 2).ToString(), Is.EqualTo("-123e2"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [TestCase(9_223_372_036_854_775_807L, 0, 9_223_372_036_854_775_807L, 0,
        TestName = "Addition_oracle_same_sign_carry")]
    [TestCase(-9_223_372_036_854_775_807L, 0, -9_223_372_036_854_775_807L, 0,
        TestName = "Addition_oracle_negative_same_sign_carry")]
    [TestCase(-922_337_203_685_477_581L, -2, 3L, -3,
        TestName = "Addition_oracle_opposite_sign_near_cancellation")]
    [TestCase(922_337_203_685_477_581L, -2, -3L, -3,
        TestName = "Addition_oracle_positive_near_cancellation")]
    [TestCase(1L, 18, 1L, 0, TestName = "Addition_oracle_exponent_gap_18")]
    [TestCase(1L, 19, 1L, 0, TestName = "Addition_oracle_exponent_gap_19")]
    [TestCase(1L, 18, -1L, 0, TestName = "Addition_oracle_opposite_sign_gap_18")]
    [TestCase(1L, 19, -1L, 0, TestName = "Addition_oracle_opposite_sign_gap_19")]
    [TestCase(1L, 20, -1L, 0, TestName = "Addition_oracle_opposite_sign_gap_20")]
    [TestCase(1L, 100, -1L, 0, TestName = "Addition_oracle_opposite_sign_gap_100")]
    [TestCase(-1L, 20, 1L, 0, TestName = "Addition_oracle_negative_opposite_sign_gap_20")]
    public void Addition_matches_independent_big_integer_oracle(
        long leftMantissa, int leftExponent, long rightMantissa, int rightExponent)
    {
        var expected = AddWithBigIntegerOracle(
            leftMantissa, leftExponent, rightMantissa, rightExponent);
        var actual = BigNum.FromParts(leftMantissa, leftExponent)
                     + BigNum.FromParts(rightMantissa, rightExponent);

        Assert.That(actual.Mantissa, Is.EqualTo(expected.Mantissa));
        Assert.That(actual.Exponent, Is.EqualTo(expected.Exponent));
    }

    [Test]
    public void Float_double_convert_explicitly()
    {
        Assert.That((BigNum)1.5, Is.EqualTo(BigNum.FromParts(15, -1)));
        Assert.That((BigNum)(-2.25), Is.EqualTo(BigNum.FromParts(-225, -2)));
        Assert.That((BigNum)100.0, Is.EqualTo((BigNum)100));
        Assert.That((BigNum)1e20, Is.EqualTo(BigNum.FromParts(1, 20)));
        Assert.That((BigNum)0.0, Is.EqualTo(BigNum.Zero));
        Assert.That((BigNum)1.5f, Is.EqualTo(BigNum.FromParts(15, -1)));
        Assert.That(() => _ = (BigNum)double.NaN, Throws.ArgumentException);
        Assert.That(() => _ = (BigNum)double.PositiveInfinity, Throws.ArgumentException);
    }

    [Test]
    public void Float_conversion_truncates_to_seven_significant_digits()
    {
        Assert.That((BigNum)12_345_678f,
            Is.EqualTo(BigNum.FromParts(1_234_567, 1)));
        Assert.That((BigNum)(-12_345_678f),
            Is.EqualTo(BigNum.FromParts(-1_234_567, 1)));
    }

    [Test]
    public void Double_conversion_preserves_sixteen_significant_digits()
    {
        const double exactSixteenDigitInteger = 1_234_567_890_123_456d;
        Assert.That((BigNum)exactSixteenDigitInteger,
            Is.EqualTo(BigNum.FromParts(1_234_567_890_123_456L, 0)));

        Assert.That((BigNum)(double)12_345_678f,
            Is.EqualTo((BigNum)12_345_678L));
    }

    [Test]
    public void Float_non_finite_values_are_rejected()
    {
        Assert.That(() => _ = (BigNum)float.NaN, Throws.ArgumentException);
        Assert.That(() => _ = (BigNum)float.PositiveInfinity, Throws.ArgumentException);
        Assert.That(() => _ = (BigNum)float.NegativeInfinity, Throws.ArgumentException);
    }

    [Test]
    public void Determinism_same_ops_same_bits()
    {
        // 연산 결과의 비트 동일성 — 결정론 계약의 최소 검증
        var x1 = BigNum.FromParts(123_456_789, 5) + BigNum.FromParts(-987, 10) - 42;
        var x2 = BigNum.FromParts(123_456_789, 5) + BigNum.FromParts(-987, 10) - 42;
        Assert.That(x1.Mantissa, Is.EqualTo(x2.Mantissa));
        Assert.That(x1.Exponent, Is.EqualTo(x2.Exponent));
    }

    private static (long Mantissa, int Exponent) AddWithBigIntegerOracle(
        long leftMantissa, int leftExponent, long rightMantissa, int rightExponent)
    {
        var exponent = Math.Min(leftExponent, rightExponent);
        var left = new BigInteger(leftMantissa)
                   * BigInteger.Pow(10, leftExponent - exponent);
        var right = new BigInteger(rightMantissa)
                    * BigInteger.Pow(10, rightExponent - exponent);
        var sum = left + right;
        if (sum.IsZero)
        {
            return (0, 0);
        }

        var negative = sum.Sign < 0;
        var magnitude = BigInteger.Abs(sum);
        var digitCount = magnitude.ToString(CultureInfo.InvariantCulture).Length;
        if (digitCount > 19)
        {
            var discardedDigits = digitCount - 19;
            magnitude /= BigInteger.Pow(10, discardedDigits);
            exponent += discardedDigits;
        }

        while (magnitude > long.MaxValue)
        {
            magnitude /= 10;
            exponent++;
        }

        var mantissa = (long)magnitude;
        while (mantissa % 10 == 0)
        {
            mantissa /= 10;
            exponent++;
        }

        return (negative ? -mantissa : mantissa, exponent);
    }
}
