using System;
using System.Numerics;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class Int128MathTests
{
    // BigInteger as the oracle: compares results against arbitrary-precision math.
    private static readonly ulong[] Samples =
    {
        0UL, 1UL, 9UL, 10UL, 999_999_999UL, 1_000_000_000UL,
        uint.MaxValue, (ulong)uint.MaxValue + 1,
        1_000_000_000_000_000_000UL,           // 10^18
        9_223_372_036_854_775_807UL,           // long.MaxValue
        ulong.MaxValue,
    };

    [Test]
    public void Mul64_matches_BigInteger_oracle()
    {
        foreach (var a in Samples)
        foreach (var b in Samples)
        {
            Int128Math.Mul64(a, b, out var hi, out var lo);
            var actual = ((BigInteger)hi << 64) | lo;
            Assert.That(actual, Is.EqualTo((BigInteger)a * b), $"{a} * {b}");
        }
    }

    [Test]
    public void DivRem_matches_BigInteger_oracle()
    {
        foreach (var a in Samples)
        foreach (var b in Samples)
        foreach (var d in Samples)
        {
            if (d == 0)
            {
                continue;
            }

            var dividend = ((BigInteger)a << 64) | b;
            Int128Math.DivRem(a, b, d, out var qHi, out var qLo, out var rem);
            var quotient = ((BigInteger)qHi << 64) | qLo;
            Assert.That(quotient, Is.EqualTo(dividend / d), $"({a}:{b}) / {d} quotient");
            Assert.That((BigInteger)rem, Is.EqualTo(dividend % d), $"({a}:{b}) / {d} remainder");
        }
    }

    [Test]
    public void DivRem_by_zero_throws()
    {
        Assert.Throws<DivideByZeroException>(() =>
            Int128Math.DivRem(1, 0, 0, out _, out _, out _));
    }
}
