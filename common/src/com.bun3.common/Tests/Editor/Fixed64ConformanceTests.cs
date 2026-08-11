using System;
using System.Buffers.Binary;
using FixedMathSharp;
using NUnit.Framework;

namespace Bun3.Common.Tests
{
    [TestFixture]
    public sealed class Fixed64ConformanceTests
    {
        private static long Raw(Fixed64 value) => value.m_rawValue;

        [Test]
        public void Representation_constants_match_q32_32()
        {
            Assert.That(Raw(Fixed64.Zero), Is.EqualTo(0L));
            Assert.That(Raw(Fixed64.One), Is.EqualTo(1L << 32));
            Assert.That(Raw(Fixed64.Half), Is.EqualTo(1L << 31));
            Assert.That(Raw(Fixed64.MinIncrement), Is.EqualTo(1L));
            Assert.That(Raw(Fixed64.MinValue), Is.EqualTo(long.MinValue));
            Assert.That(Raw(Fixed64.MaxValue), Is.EqualTo(long.MaxValue));
        }

        [TestCase(0L)]
        [TestCase(1L)]
        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void FromRaw_preserves_every_input_bit(long raw)
        {
            Assert.That(Raw(Fixed64.FromRaw(raw)), Is.EqualTo(raw));
        }

        [Test]
        public void Multiply_and_divide_midpoints_round_to_even()
        {
            Assert.That(Raw(Fixed64.FromRaw(1) * Fixed64.Half), Is.EqualTo(0L));
            Assert.That(Raw(Fixed64.FromRaw(3) * Fixed64.Half), Is.EqualTo(2L));
            Assert.That(Raw(Fixed64.FromRaw(1) / Fixed64.Two), Is.EqualTo(0L));
            Assert.That(Raw(Fixed64.FromRaw(3) / Fixed64.Two), Is.EqualTo(2L));
        }

        [Test]
        public void Negative_midpoints_round_to_even()
        {
            // pinned upstream 7.0.0 reference harness + Q32.32 hand check
            Assert.That(Raw(Fixed64.FromRaw(-1) * Fixed64.Half), Is.EqualTo(0L));
            Assert.That(Raw(Fixed64.FromRaw(-3) * Fixed64.Half), Is.EqualTo(-2L));
            Assert.That(Raw(Fixed64.FromRaw(-1) / Fixed64.Two), Is.EqualTo(0L));
            Assert.That(Raw(Fixed64.FromRaw(-3) / Fixed64.Two), Is.EqualTo(-2L));
        }

        [Test]
        public void Add_and_subtract_saturate_at_raw_extrema()
        {
            // pinned upstream 7.0.0 reference harness + Q32.32 hand check
            Assert.That(Raw(Fixed64.MaxValue + Fixed64.One),
                Is.EqualTo(9_223_372_036_854_775_807L));
            Assert.That(Raw(Fixed64.MinValue - Fixed64.One),
                Is.EqualTo(-9_223_372_036_854_775_808L));
        }

        [Test]
        public void Multiply_overflow_paths_saturate_deterministically()
        {
            Assert.That(Fixed64.MaxValue * Fixed64.Two, Is.EqualTo(Fixed64.MaxValue));
            Assert.That(Fixed64.MinValue * Fixed64.Two, Is.EqualTo(Fixed64.MinValue));
            Assert.That(Fixed64.MinValue / Fixed64.NegOne, Is.EqualTo(Fixed64.MaxValue));
        }

        [Test]
        public void Intermediate_multiply_and_divide_edges_preserve_q32_32_rounding()
        {
            // pinned upstream 7.0.0 reference harness + Q32.32 hand check
            Assert.That(Raw(Fixed64.MaxValue * Fixed64.Half),
                Is.EqualTo(4_611_686_018_427_387_904L));
            Assert.That(Raw(Fixed64.MinValue * Fixed64.Half),
                Is.EqualTo(-4_611_686_018_427_387_904L));
            Assert.That(Raw(Fixed64.MaxValue / Fixed64.Two),
                Is.EqualTo(4_611_686_018_427_387_904L));
            Assert.That(Raw(Fixed64.MinValue / Fixed64.Two),
                Is.EqualTo(-4_611_686_018_427_387_904L));
        }

        [Test]
        public void Signed_fractional_arithmetic_matches_raw_goldens()
        {
            // pinned upstream 7.0.0 reference harness + Q32.32 hand check
            var negative = Fixed64.FromRaw(-6_442_450_944L); // -1.5
            var positive = Fixed64.FromRaw(9_663_676_416L);  // 2.25

            Assert.That(Raw(negative + positive), Is.EqualTo(3_221_225_472L));
            Assert.That(Raw(negative - positive), Is.EqualTo(-16_106_127_360L));
            Assert.That(Raw(negative * positive), Is.EqualTo(-14_495_514_624L));
            Assert.That(Raw(negative / positive), Is.EqualTo(-2_863_311_531L));
        }

        [Test]
        public void Floating_boundary_rejects_non_finite_and_out_of_range_values()
        {
            Assert.That(() => Fixed64.FromDouble(double.NaN),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Fixed64.FromDouble(double.PositiveInfinity),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Fixed64.FromDouble(double.MaxValue),
                Throws.TypeOf<OverflowException>());
        }

        [Test]
        public void Nontrivial_scalar_and_vector_math_match_raw_goldens()
        {
            // pinned upstream 7.0.0 reference harness + Q32.32 hand check
            Assert.That(Raw(FixedMath.Sin((Fixed64)1)), Is.EqualTo(3_614_090_365L));
            Assert.That(Raw(FixedMath.Cos((Fixed64)1)), Is.EqualTo(2_320_580_735L));
            Assert.That(Raw(FixedMath.Sqrt((Fixed64)2)), Is.EqualTo(6_074_001_000L));

            var normalized = new Vector2d(3, 4).Normalized;
            Assert.That(Raw(normalized.X), Is.EqualTo(2_576_980_378L));
            Assert.That(Raw(normalized.Y), Is.EqualTo(3_435_973_837L));
        }

        [Test]
        public void Six_hundred_fixed_ticks_accumulate_the_same_raw_position()
        {
            // pinned upstream 7.0.0 reference harness + Q32.32 hand check
            var delta = Fixed64.FromDouble(1d / 60d);
            var speed = Fixed64.FromDouble(6.25d);
            Assert.That(Raw(delta), Is.EqualTo(71_582_788L));
            Assert.That(Raw(speed), Is.EqualTo(26_843_545_600L));

            var step = speed * delta;
            var position = Fixed64.Zero;

            for (var i = 0; i < 600; i++)
            {
                position += step;
            }

            Assert.That(Raw(step), Is.EqualTo(447_392_425L));
            Assert.That(Raw(position), Is.EqualTo(268_435_455_000L));
        }

        [Test]
        public void Raw_state_hash_bytes_are_little_endian_signed_64_bit()
        {
            // pinned upstream 7.0.0 reference harness + Q32.32 hand check
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(
                bytes, Raw(Fixed64.FromRaw(-4_294_967_297L)));

            Assert.That(bytes.ToArray(),
                Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF }));
        }
    }
}
