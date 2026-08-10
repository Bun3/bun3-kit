using System;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class BigNumFormatTests
{
    private static string Format(BigNum value, BigNumFormat? format = null)
    {
        Span<char> buffer = stackalloc char[64];
        Assert.That(value.TryFormat(buffer, out var written, format), Is.True);
        return new string(buffer[..written]);
    }

    [Test]
    public void Small_integers_render_plain()
    {
        Assert.That(Format(0), Is.EqualTo("0"));
        Assert.That(Format(42), Is.EqualTo("42"));
        Assert.That(Format(-42), Is.EqualTo("-42"));
        Assert.That(Format(999), Is.EqualTo("999"));
    }

    [Test]
    public void Small_fractions_render_with_decimal_point()
    {
        Assert.That(Format(BigNum.FromParts(125, -2)), Is.EqualTo("1.25"));
        Assert.That(Format(BigNum.FromParts(-5, -1)), Is.EqualTo("-0.5"));
    }

    [Test]
    public void Alpha_units_group_by_thousands()
    {
        Assert.That(Format((BigNum)1_500), Is.EqualTo("1.5K"));
        Assert.That(Format((BigNum)2_000_000), Is.EqualTo("2M"));
        Assert.That(Format((BigNum)3_450_000_000L), Is.EqualTo("3.45B"));
        Assert.That(Format((BigNum)7_000_000_000_000L), Is.EqualTo("7T"));
        Assert.That(Format((BigNum)(-1_500)), Is.EqualTo("-1.5K"));
    }

    [Test]
    public void Korean_units_group_by_ten_thousands()
    {
        var fmt = BigNumFormat.Korean;
        Assert.That(Format((BigNum)15_000, fmt), Is.EqualTo("1.5만"));
        Assert.That(Format((BigNum)200_000_000, fmt), Is.EqualTo("2억"));
        Assert.That(Format((BigNum)3_000_000_000_000L, fmt), Is.EqualTo("3조"));
        Assert.That(Format(BigNum.FromParts(92, 17), fmt), Is.EqualTo("920경"));
    }

    [Test]
    public void Fraction_truncates_to_two_digits_and_strips_zeros()
    {
        Assert.That(Format((BigNum)1_234), Is.EqualTo("1.23K"));     // 1.234 → 1.23 절사
        Assert.That(Format((BigNum)1_204), Is.EqualTo("1.2K"));      // 트레일링 0 제거
        Assert.That(Format((BigNum)1_004), Is.EqualTo("1K"));        // .00 제거
    }

    [Test]
    public void Beyond_unit_table_falls_back_to_scientific()
    {
        Assert.That(Format(BigNum.FromParts(123, 43)), Is.EqualTo("1.23e45"));
        Assert.That(Format(BigNum.FromParts(-123, 43)), Is.EqualTo("-1.23e45"));
        Assert.That(Format(BigNum.FromParts(1, 100)), Is.EqualTo("1e100"));
    }

    [Test]
    public void Custom_unit_override_is_respected()
    {
        // 스펙 §6: 단위 글자는 설정으로 오버라이드
        var custom = new BigNumFormat(3, new[] { "", "k", "m" });
        Assert.That(Format((BigNum)1_500, custom), Is.EqualTo("1.5k"));
        Assert.That(Format((BigNum)2_000_000, custom), Is.EqualTo("2m"));
        Assert.That(Format(BigNum.FromParts(3, 9), custom), Is.EqualTo("3e9"));   // 테이블 초과 → 폴백
    }

    [Test]
    public void Insufficient_destination_returns_false()
    {
        Span<char> tiny = stackalloc char[2];
        Assert.That(((BigNum)12_345).TryFormat(tiny, out _, null), Is.False);
    }

    [Test]
    public void Plain_fraction_truncates_to_two_digits()
    {
        Assert.That(Format(BigNum.FromParts(523, -4)), Is.EqualTo("0.05"));    // 0.0523 절사
        Assert.That(Format(BigNum.FromParts(10001, -4)), Is.EqualTo("1"));     // 1.0001 → .00 제거
    }

    [Test]
    public void Tiny_values_fall_back_to_scientific_with_negative_exponent()
    {
        Assert.That(Format(BigNum.FromParts(1, -50)), Is.EqualTo("1e-50"));
        Assert.That(Format(BigNum.FromParts(123, -45)), Is.EqualTo("1.23e-43"));
        Assert.That(Format(BigNum.FromParts(-1, -50)), Is.EqualTo("-1e-50"));
    }
}
