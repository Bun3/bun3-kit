using System;
using System.Numerics;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests
{
    /// <summary>
    /// Checks random inputs against a BigInteger rational oracle. Seeds are fixed so failures
    /// reproduce; also verifies 18-significant-digit preservation (relative error 1e-17) and
    /// canonical-form invariants.
    /// </summary>
    [TestFixture]
    public sealed class BigNumDifferentialTests
    {
        private const int Iterations = 5_000;

        // Relative-error tolerance: |actual - oracle| * 10^17 <= |oracle|
        private static readonly BigInteger Tolerance = BigInteger.Pow(10, 17);

        [TestCase(20260815, 0, 20, TestName = "Differential_nonnegative_exponents")]
        [TestCase(20260816, -25, 25, TestName = "Differential_signed_exponents")]
        public void Arithmetic_matches_BigInteger_oracle(int seed, int minExponent, int maxExponent)
        {
            var random = new Random(seed);
            for (var iteration = 0; iteration < Iterations; iteration++)
            {
                var (leftMantissa, leftExponent) = NextOperand(random, minExponent, maxExponent);
                var (rightMantissa, rightExponent) = NextOperand(random, minExponent, maxExponent);
                var left = BigNum.FromParts(leftMantissa, leftExponent);
                var right = BigNum.FromParts(rightMantissa, rightExponent);

                var (leftNumerator, leftDenominator) = Rational(leftMantissa, leftExponent);
                var (rightNumerator, rightDenominator) = Rational(rightMantissa, rightExponent);

                AssertMatches(
                    "add", left + right,
                    leftNumerator * rightDenominator + rightNumerator * leftDenominator,
                    leftDenominator * rightDenominator);
                AssertMatches(
                    "subtract", left - right,
                    leftNumerator * rightDenominator - rightNumerator * leftDenominator,
                    leftDenominator * rightDenominator);
                AssertMatches(
                    "multiply", left * right,
                    leftNumerator * rightNumerator,
                    leftDenominator * rightDenominator);
                if (rightMantissa != 0)
                {
                    AssertMatches(
                        "divide", left / right,
                        leftNumerator * rightDenominator,
                        leftDenominator * rightNumerator);
                }

                var oracleOrder = (leftNumerator * rightDenominator)
                    .CompareTo(rightNumerator * leftDenominator);
                Assert.That(
                    Math.Sign(left.CompareTo(right)), Is.EqualTo(Math.Sign(oracleOrder)),
                    $"order mismatch: {leftMantissa}e{leftExponent} vs {rightMantissa}e{rightExponent}");

                Assert.That(
                    (left + (-left)).IsZero, Is.True,
                    $"a + (-a) is not zero: {leftMantissa}e{leftExponent}");
            }
        }

        [Test]
        public void Far_gap_addition_keeps_the_larger_operand_significant_digits()
        {
            // Digit gap beyond the 18-digit window: same sign absorbs, opposite sign borrows 1.
            var large = BigNum.FromParts(1, 30);
            Assert.That(large + BigNum.FromParts(1, 0), Is.EqualTo(large));

            var borrowed = large + BigNum.FromParts(-1, 0);
            Assert.That(borrowed.Sign, Is.EqualTo(1));
            Assert.That(borrowed, Is.LessThan(large));

            var oracle = BigInteger.Pow(10, 30) - 1;
            var (numerator, denominator) = Rational(borrowed.Mantissa, borrowed.Exponent);
            AssertWithinTolerance("far-gap", numerator, denominator, oracle, BigInteger.One);
        }

        private static (long Mantissa, int Exponent) NextOperand(
            Random random, int minExponent, int maxExponent)
        {
            var digits = random.Next(1, 20);
            var lower = BigInteger.Pow(10, digits - 1);
            var value = lower + RandomBelow(random, BigInteger.Pow(10, digits) - lower);
            if (value > long.MaxValue) value = long.MaxValue;

            var mantissa = (long)value;
            if (random.Next(2) == 0) mantissa = -mantissa;
            if (random.Next(20) == 0) mantissa = 0;
            return (mantissa, random.Next(minExponent, maxExponent + 1));
        }

        private static BigInteger RandomBelow(Random random, BigInteger exclusiveUpper)
        {
            var bytes = exclusiveUpper.ToByteArray();
            random.NextBytes(bytes);
            bytes[bytes.Length - 1] = 0;   // clear the sign bit to keep the value positive
            return new BigInteger(bytes) % exclusiveUpper;
        }

        private static (BigInteger Numerator, BigInteger Denominator) Rational(
            long mantissa, int exponent) =>
            exponent >= 0
                ? (new BigInteger(mantissa) * BigInteger.Pow(10, exponent), BigInteger.One)
                : (new BigInteger(mantissa), BigInteger.Pow(10, -exponent));

        private static void AssertMatches(
            string label, BigNum actual, BigInteger numerator, BigInteger denominator)
        {
            if (actual.IsZero)
            {
                Assert.That(actual.Exponent, Is.Zero, $"{label}: zero exponent not canonical");
            }
            else
            {
                Assert.That(
                    actual.Mantissa % 10, Is.Not.Zero,
                    $"{label}: mantissa has trailing zeros ({actual})");
            }

            var (actualNumerator, actualDenominator) = Rational(actual.Mantissa, actual.Exponent);
            AssertWithinTolerance(label, actualNumerator, actualDenominator, numerator, denominator);
        }

        private static void AssertWithinTolerance(
            string label,
            BigInteger actualNumerator,
            BigInteger actualDenominator,
            BigInteger oracleNumerator,
            BigInteger oracleDenominator)
        {
            var left = actualNumerator * oracleDenominator;
            var right = oracleNumerator * actualDenominator;
            var difference = BigInteger.Abs(left - right);
            if (difference.IsZero) return;

            Assert.That(
                oracleNumerator.IsZero, Is.False,
                $"{label}: oracle is zero but result is not");
            Assert.That(
                difference * Tolerance <= BigInteger.Abs(right), Is.True,
                $"{label}: relative error exceeds 1e-17");
        }
    }
}
