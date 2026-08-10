using System;
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

        // 실제 오버플로 시 한 자리 양보 (양쪽 절사 후 합)
        Assert.That((BigNum)long.MaxValue + long.MaxValue,
            Is.EqualTo(BigNum.FromParts(184_467_440_737_095_516L, 2)));
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
}
