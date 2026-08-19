#nullable enable
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class BigNumParseTests
{
    [TestCase("50", 50, 0)]
    [TestCase("-1.5", -15, -1)]
    [TestCase("0.3", 3, -1)]
    [TestCase("1.23e45", 123, 43)]
    [TestCase("0", 0, 0)]
    [TestCase("00.50", 5, -1)]
    public void Parses_expected_value(string text, long mantissa, int exponent)
    {
        Assert.That(BigNum.TryParse(text, out var value), Is.True);
        Assert.That(value, Is.EqualTo(BigNum.FromParts(mantissa, exponent)));
    }

    [Test]
    public void Excess_digits_beyond_long_safe_range_shift_into_exponent_and_truncate()
    {
        // 22자리 9 — long.MaxValue(9223372036854775807) 안전 범위는 9가 18자리까지만 담기고
        // (19번째 9부터는 mantissa*10+9가 long.MaxValue를 넘는다), 나머지 4자리는 지수로 밀려
        // 0 방향 절사된다. BigNum.Mantissa는 long이라 19자리 전부가 항상 담기지는 않는다 —
        // MantissaMaxDigits(19)는 상한일 뿐 실제 정확 한계는 long.MaxValue다(BigNum.cs 주석 참고).
        Assert.That(BigNum.TryParse("9999999999999999999999", out var value), Is.True);
        Assert.That(value, Is.EqualTo(BigNum.FromParts(999999999999999999, 4)));
    }

    [Test]
    public void Excess_digits_within_long_safe_range_shift_into_exponent()
    {
        // 23자리, 선두 숫자가 작아 앞 19자리가 long 범위에 안전하게 담기는 경우.
        Assert.That(BigNum.TryParse("12345678901234567890123", out var value), Is.True);
        Assert.That(value, Is.EqualTo(BigNum.FromParts(1234567890123456789, 4)));
    }

    [TestCase("")]
    [TestCase("abc")]
    [TestCase("1.2.3")]
    [TestCase("1e")]
    [TestCase(".5")]
    [TestCase("1.")]
    [TestCase("+5")]
    [TestCase("1e999999999")]
    public void Rejects_invalid_input(string text)
    {
        Assert.That(BigNum.TryParse(text, out var value), Is.False);
        Assert.That(value, Is.EqualTo(BigNum.Zero));
    }
}
