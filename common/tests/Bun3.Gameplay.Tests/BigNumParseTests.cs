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
        // 22 nines: only 18 fit within long.MaxValue (the 19th would push mantissa*10+9 past it),
        // so the remaining 4 digits shift into the exponent, truncating toward zero.
        // MantissaMaxDigits(19) is an upper bound; the exact limit is long.MaxValue.
        Assert.That(BigNum.TryParse("9999999999999999999999", out var value), Is.True);
        Assert.That(value, Is.EqualTo(BigNum.FromParts(999999999999999999, 4)));
    }

    [Test]
    public void Excess_digits_within_long_safe_range_shift_into_exponent()
    {
        // 23 digits with small leading digits, so the first 19 fit safely in long range.
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
