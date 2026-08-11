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
        public void Overflow_paths_saturate_deterministically()
        {
            Assert.That(Fixed64.MaxValue * Fixed64.Two, Is.EqualTo(Fixed64.MaxValue));
            Assert.That(Fixed64.MinValue * Fixed64.Two, Is.EqualTo(Fixed64.MinValue));
            Assert.That(Fixed64.MinValue / Fixed64.NegOne, Is.EqualTo(Fixed64.MaxValue));
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
        public void Scalar_and_vector_math_have_exact_anchor_results()
        {
            Assert.That(Raw(FixedMath.Sqrt((Fixed64)4)), Is.EqualTo(2L << 32));
            Assert.That(Raw(FixedMath.Sin(Fixed64.Zero)), Is.EqualTo(0L));
            Assert.That(Raw(FixedMath.Cos(Fixed64.Zero)), Is.EqualTo(1L << 32));

            var normalized = new Vector2d(3, 0).Normalized;
            Assert.That(Raw(normalized.X), Is.EqualTo(1L << 32));
            Assert.That(Raw(normalized.Y), Is.EqualTo(0L));
        }

        [Test]
        public void Six_hundred_fixed_ticks_accumulate_the_same_raw_position()
        {
            var delta = Fixed64.FromRaw(71_582_788L); // round(2^32 / 60)
            var step = (Fixed64)6 * delta;
            var position = Fixed64.Zero;

            for (var i = 0; i < 600; i++)
            {
                position += step;
            }

            Assert.That(Raw(step), Is.EqualTo(429_496_728L));
            Assert.That(Raw(position), Is.EqualTo(257_698_036_800L));
        }

        [Test]
        public void Raw_state_hash_bytes_are_little_endian_signed_64_bit()
        {
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, Raw(Fixed64.One));

            Assert.That(bytes.ToArray(),
                Is.EqualTo(new byte[] { 0, 0, 0, 0, 1, 0, 0, 0 }));
        }
    }
}
