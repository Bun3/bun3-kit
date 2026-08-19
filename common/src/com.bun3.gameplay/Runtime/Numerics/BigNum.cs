#nullable enable
using System;
using System.Globalization;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// Deterministic decimal number: value = Mantissa × 10^Exponent. Integer-only arithmetic
    /// yields bit-identical results on every platform. 18-19 significant digits — exact for
    /// integers within long range (±9.2×10^18); beyond that, approximate (low digits truncated
    /// toward zero).
    /// Canonical form: Mantissa==0 implies Exponent==0; otherwise Mantissa is not a multiple
    /// of 10 — equal values always share identical bits, so equality/hash are field comparisons.
    /// </summary>
    public readonly partial struct BigNum : IEquatable<BigNum>, IComparable<BigNum>
    {
        /// <summary>Exponent limit. Exceeding it throws <see cref="BigNumOverflowException"/>; below the negative limit underflows to zero.</summary>
        public const int MaxExponent = 100_000_000;

        // Max decimal significant digits a long mantissa holds — source of the integer-exact limit (±9.2×10^18).
        private const int MantissaMaxDigits = 19;

        // Internal scale digits (= 10^18) — division numerator widening and addition alignment window.
        private const int ScaleDigits = MantissaMaxDigits - 1;

        // Max decimal digits of a 128-bit value — digit-table size.
        private const int MaxDigits128 = 39;

        // 10^0 .. 10^MantissaMaxDigits (10^19 fits in ulong)
        private static readonly ulong[] Pow10 = BuildPow10();

        // 128-bit representation of 10^i (i = 0..38) — for digit counting of 128-bit values
        private static readonly ulong[] Pow10Hi128 = new ulong[MaxDigits128];
        private static readonly ulong[] Pow10Lo128 = new ulong[MaxDigits128];

        static BigNum()
        {
            Pow10Lo128[0] = 1;
            for (var i = 1; i < MaxDigits128; i++)
            {
                // (hi:lo) × 10 — fold the low-word carry into hi
                Int128Math.Mul64(Pow10Lo128[i - 1], 10, out var carry, out var lo);
                Pow10Lo128[i] = lo;
                Pow10Hi128[i] = Pow10Hi128[i - 1] * 10 + carry;
            }
        }

        private static ulong[] BuildPow10()
        {
            var table = new ulong[MantissaMaxDigits + 1];
            table[0] = 1;
            for (var i = 1; i < table.Length; i++)
            {
                table[i] = table[i - 1] * 10;
            }

            return table;
        }

        private static int CountDigits64(ulong value)
        {
            // Branch tree, 4-5 comparisons — hot-path saving over a linear scan (up to 19)
            if (value < 10_000_000_000UL)   // < 10^10
            {
                if (value < 100_000UL)
                {
                    if (value < 100UL)
                    {
                        return value < 10UL ? 1 : 2;
                    }

                    return value < 1_000UL ? 3 : (value < 10_000UL ? 4 : 5);
                }

                if (value < 10_000_000UL)
                {
                    return value < 1_000_000UL ? 6 : 7;
                }

                return value < 100_000_000UL ? 8 : (value < 1_000_000_000UL ? 9 : 10);
            }

            if (value < 1_000_000_000_000_000UL)   // < 10^15
            {
                if (value < 1_000_000_000_000UL)
                {
                    return value < 100_000_000_000UL ? 11 : 12;
                }

                return value < 10_000_000_000_000UL ? 13 : (value < 100_000_000_000_000UL ? 14 : 15);
            }

            if (value < 100_000_000_000_000_000UL)
            {
                return value < 10_000_000_000_000_000UL ? 16 : 17;
            }

            return value < 1_000_000_000_000_000_000UL
                ? 18
                : (value < 10_000_000_000_000_000_000UL ? 19 : 20);
        }

        private static int CountDigits128(ulong hi, ulong lo)
        {
            if (hi == 0)
            {
                return CountDigits64(lo);
            }

            // hi != 0 ⇒ value ≥ 2^64 > 10^19 ⇒ digits ∈ [20, 39]. Binary-search the smallest d with value < 10^d.
            var low = MantissaMaxDigits + 1;
            var high = MaxDigits128;
            while (low < high)
            {
                var mid = (low + high) >> 1;
                if (hi > Pow10Hi128[mid] || (hi == Pow10Hi128[mid] && lo >= Pow10Lo128[mid]))
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        /// <summary>Mantissa. In canonical form, not a multiple of 10 (except zero).</summary>
        public readonly long Mantissa;

        /// <summary>Decimal exponent.</summary>
        public readonly int Exponent;

        /// <summary>Zero.</summary>
        public static readonly BigNum Zero = default;

        /// <summary>One.</summary>
        public static readonly BigNum One = new BigNum(1, 0);

        /// <summary>Smallest representable value: <c>-long.MaxValue × 10^MaxExponent</c>.</summary>
        public static readonly BigNum MinValue = new BigNum(-long.MaxValue, MaxExponent);

        /// <summary>Largest representable value: <c>long.MaxValue × 10^MaxExponent</c>.</summary>
        public static readonly BigNum MaxValue = new BigNum(long.MaxValue, MaxExponent);

        private BigNum(long mantissa, int exponent)
        {
            Mantissa = mantissa;
            Exponent = exponent;
        }

        /// <summary>Creates a value from mantissa × 10^exponent. Canonicalizes; throws when the exponent limit is exceeded.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="mantissa"/> is <see cref="long.MinValue"/>, outside the symmetric mantissa range.
        /// </exception>
        public static BigNum FromParts(long mantissa, int exponent) =>
            Canonicalize(mantissa, exponent);

        /// <summary>long integers convert exactly.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="value"/> is <see cref="long.MinValue"/>, outside the symmetric mantissa range.
        /// </exception>
        public static implicit operator BigNum(long value) => Canonicalize(value, 0);

        /// <summary>int integers convert exactly.</summary>
        public static implicit operator BigNum(int value) => Canonicalize(value, 0);

        // double conversion normalization window [1e15, 1e16) — secures 16 significant digits
        private const double DoubleNormalizeLow = 1e15;
        private const double DoubleNormalizeHigh = 1e16;

        private const float FloatNormalizeLow = 1e6f;
        private const float FloatNormalizeHigh = 1e7f;

        /// <summary>Truncating conversion from double (~16 significant digits) — **explicit**: lossy,
        /// and the cast stops runtime floating-point values from leaking into the sim (determinism
        /// boundary). Intended for one-time conversion at boundaries such as data loading.
        /// NaN/Infinity throw. The conversion uses only basic IEEE ops (×10/÷10), so identical
        /// input bits give identical results everywhere.</summary>
        public static explicit operator BigNum(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("NaN/Infinity cannot be converted to BigNum.", nameof(value));
            }

            if (value == 0d)
            {
                return Zero;
            }

            var negative = value < 0d;
            var abs = negative ? -value : value;
            var exponent = 0L;

            while (abs >= DoubleNormalizeHigh)
            {
                abs /= 10d;
                exponent++;
            }

            while (abs < DoubleNormalizeLow)
            {
                abs *= 10d;
                exponent--;
            }

            var mantissa = (long)abs;   // truncate toward zero
            return Canonicalize(negative ? -mantissa : mantissa, exponent);
        }

        /// <summary>Truncating conversion from float (~7 significant digits) — explicit. Same rules as double.</summary>
        public static explicit operator BigNum(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentException("NaN/Infinity cannot be converted to BigNum.", nameof(value));
            }

            if (value == 0f)
            {
                return Zero;
            }

            var negative = value < 0f;
            var abs = negative ? -value : value;
            var exponent = 0L;

            while (abs >= FloatNormalizeHigh)
            {
                abs /= 10f;
                exponent++;
            }

            while (abs < FloatNormalizeLow)
            {
                abs *= 10f;
                exponent--;
            }

            var mantissa = (long)abs;
            return Canonicalize(negative ? -mantissa : mantissa, exponent);
        }

        /// <summary>Whether the value is zero.</summary>
        public bool IsZero => Mantissa == 0;

        /// <summary>Sign: -1, 0, +1.</summary>
        public int Sign => Math.Sign(Mantissa);

        private static BigNum Canonicalize(long mantissa, long exponent)
        {
            if (mantissa == 0)
            {
                return default;
            }

            if (mantissa == long.MinValue)
            {
                // The only value whose absolute negation overflows — outside the symmetric mantissa range
                throw new ArgumentOutOfRangeException(
                    nameof(mantissa), mantissa,
                    "BigNum mantissa must be at least -long.MaxValue.");
            }

            if (mantissa % 10 == 0)
            {
                // Strip trailing zeros — 8→4→2→1 ladder cuts division count (result is order-independent)
                while (mantissa % 100_000_000 == 0)
                {
                    mantissa /= 100_000_000;
                    exponent += 8;
                }

                while (mantissa % 10_000 == 0)
                {
                    mantissa /= 10_000;
                    exponent += 4;
                }

                while (mantissa % 100 == 0)
                {
                    mantissa /= 100;
                    exponent += 2;
                }

                while (mantissa % 10 == 0)
                {
                    mantissa /= 10;
                    exponent++;
                }
            }

            if (exponent > MaxExponent)
            {
                throw new BigNumOverflowException(exponent);
            }

            if (exponent < -MaxExponent)
            {
                return default;   // underflow — tiny magnitudes converge to zero
            }

            return new BigNum(mantissa, (int)exponent);
        }

        /// <summary>
        /// Addition. Same-sign terms outside the preserved window are ignored; opposite-sign
        /// terms apply a borrow, then truncate toward zero.
        /// </summary>
        public static BigNum operator +(BigNum a, BigNum b)
        {
            if (a.IsZero)
            {
                return b;
            }

            if (b.IsZero)
            {
                return a;
            }

            var aNegative = a.Mantissa < 0;
            var bNegative = b.Mantissa < 0;
            var aMagnitude = (ulong)(aNegative ? -a.Mantissa : a.Mantissa);
            var bMagnitude = (ulong)(bNegative ? -b.Mantissa : b.Mantissa);
            var aDecimalMagnitude = (long)CountDigits64(aMagnitude) + a.Exponent - 1;
            var bDecimalMagnitude = (long)CountDigits64(bMagnitude) + b.Exponent - 1;

            var magnitudeDifference = aDecimalMagnitude - bDecimalMagnitude;
            if (magnitudeDifference > ScaleDigits)
            {
                return aNegative == bNegative ? a : SubtractFarMagnitude(a);
            }

            if (magnitudeDifference < -ScaleDigits)
            {
                return aNegative == bNegative ? b : SubtractFarMagnitude(b);
            }

            var exponent = Math.Min(a.Exponent, b.Exponent);
            ScaleMantissa128(aMagnitude, a.Exponent - exponent, out var aHi, out var aLo);
            ScaleMantissa128(bMagnitude, b.Exponent - exponent, out var bHi, out var bLo);

            ulong resultHi;
            ulong resultLo;
            bool resultNegative;
            if (aNegative == bNegative)
            {
                Add128(aHi, aLo, bHi, bLo, out resultHi, out resultLo);
                resultNegative = aNegative;
            }
            else
            {
                var comparison = Compare128(aHi, aLo, bHi, bLo);
                if (comparison == 0)
                {
                    return Zero;
                }

                if (comparison > 0)
                {
                    Subtract128(aHi, aLo, bHi, bLo, out resultHi, out resultLo);
                    resultNegative = aNegative;
                }
                else
                {
                    Subtract128(bHi, bLo, aHi, aLo, out resultHi, out resultLo);
                    resultNegative = bNegative;
                }
            }

            long resultExponent = exponent;
            var mantissa = ReduceToLong(resultHi, resultLo, ref resultExponent);
            return Canonicalize(resultNegative ? -mantissa : mantissa, resultExponent);
        }

        private static BigNum SubtractFarMagnitude(BigNum larger)
        {
            var negative = larger.Mantissa < 0;
            var magnitude = (ulong)(negative ? -larger.Mantissa : larger.Mantissa);
            var digitCount = CountDigits64(magnitude);

            long retainedExponent = (long)larger.Exponent + digitCount - MantissaMaxDigits;
            var decimalShift = (int)((long)larger.Exponent - retainedExponent);
            var retainedMantissa = magnitude * Pow10[decimalShift];

            if (retainedMantissa > long.MaxValue)
            {
                retainedMantissa /= 10;
                retainedExponent++;
            }

            retainedMantissa--; // nonzero smaller operand below the retained window borrows exactly one
            var signedMantissa = negative ? -(long)retainedMantissa : (long)retainedMantissa;
            return Canonicalize(signedMantissa, retainedExponent);
        }

        /// <summary>Subtraction.</summary>
        public static BigNum operator -(BigNum a, BigNum b) => a + (-b);

        /// <summary>Negation.</summary>
        public static BigNum operator -(BigNum value) =>
            value.IsZero ? value : new BigNum(-value.Mantissa, value.Exponent);

        // = Pow10[ScaleDigits] — array reference not allowed in a const context
        private const ulong TenPow18 = 1_000_000_000_000_000_000UL;

        /// <summary>Multiplication. Result truncated (toward zero) to 18-19 significant digits.</summary>
        public static BigNum operator *(BigNum a, BigNum b)
        {
            if (a.IsZero || b.IsZero)
            {
                return Zero;
            }

            var negative = (a.Mantissa < 0) != (b.Mantissa < 0);
            var ua = (ulong)Math.Abs(a.Mantissa);
            var ub = (ulong)Math.Abs(b.Mantissa);

            Int128Math.Mul64(ua, ub, out var hi, out var lo);
            var exponent = (long)a.Exponent + b.Exponent;
            var mantissa = ReduceToLong(hi, lo, ref exponent);
            return Canonicalize(negative ? -mantissa : mantissa, exponent);
        }

        /// <summary>Division. Result truncated (toward zero) to 17-19 significant digits. Throws on division by zero.</summary>
        public static BigNum operator /(BigNum a, BigNum b)
        {
            if (b.IsZero)
            {
                throw new DivideByZeroException();
            }

            if (a.IsZero)
            {
                return Zero;
            }

            var negative = (a.Mantissa < 0) != (b.Mantissa < 0);
            var ua = (ulong)Math.Abs(a.Mantissa);
            var ub = (ulong)Math.Abs(b.Mantissa);
            var exponent = (long)a.Exponent - b.Exponent - ScaleDigits;

            // Normalize both mantissas into [10^18, 10^19) — fixed-width scaling collapses
            // significant digits for small/large mantissa mixes, and the quotient collapses to
            // zero when the divisor mantissa exceeds numerator × 10^18.
            // Digit-count-based bulk scale: one ×10^k (identical result to a loop).
            var scaleA = MantissaMaxDigits - CountDigits64(ua);
            ua *= Pow10[scaleA];
            exponent -= scaleA;

            var scaleB = MantissaMaxDigits - CountDigits64(ub);
            ub *= Pow10[scaleB];
            exponent += scaleB;

            Int128Math.Mul64(ua, TenPow18, out var hi, out var lo);
            Int128Math.DivRem(hi, lo, ub, out var qHi, out var qLo, out _);
            var mantissa = ReduceToLong(qHi, qLo, ref exponent);
            return Canonicalize(negative ? -mantissa : mantissa, exponent);
        }

        // Reduces a 128-bit value into long range (≤ long.MaxValue) by 10^k truncation, adding k
        // to exponent. A single 10^k division is bit-identical to k successive /10 truncations
        // (integer-division composition). k comes only from the digit count — cutting more than
        // needed destroys significant digits.
        private static long ReduceToLong(ulong hi, ulong lo, ref long exponent)
        {
            if (hi == 0 && lo <= long.MaxValue)
            {
                return (long)lo;   // no reduction needed — fast path for the common gameplay band
            }

            var digits = CountDigits128(hi, lo);
            if (digits > MantissaMaxDigits)
            {
                var k = Math.Min(digits - MantissaMaxDigits, MantissaMaxDigits);   // Pow10 table cap (10^19)
                Int128Math.DivRem(hi, lo, Pow10[k], out hi, out lo, out _);
                exponent += k;
            }

            // A 19-digit quotient can exceed long.MaxValue (9.22e18) — at most 1-2 corrections
            while (hi != 0 || lo > long.MaxValue)
            {
                Int128Math.DivRem(hi, lo, 10, out hi, out lo, out _);
                exponent++;
            }

            return (long)lo;
        }

        private static void Add128(
            ulong leftHi, ulong leftLo, ulong rightHi, ulong rightLo,
            out ulong resultHi, out ulong resultLo)
        {
            resultLo = unchecked(leftLo + rightLo);
            var carry = resultLo < leftLo ? 1UL : 0UL;
            resultHi = unchecked(leftHi + rightHi + carry);
        }

        private static int Compare128(
            ulong leftHi, ulong leftLo, ulong rightHi, ulong rightLo)
        {
            if (leftHi != rightHi)
            {
                return leftHi < rightHi ? -1 : 1;
            }

            if (leftLo == rightLo)
            {
                return 0;
            }

            return leftLo < rightLo ? -1 : 1;
        }

        private static void Subtract128(
            ulong largerHi, ulong largerLo, ulong smallerHi, ulong smallerLo,
            out ulong resultHi, out ulong resultLo)
        {
            var borrow = largerLo < smallerLo ? 1UL : 0UL;
            resultLo = unchecked(largerLo - smallerLo);
            resultHi = unchecked(largerHi - smallerHi - borrow);
        }

        private static void ScaleMantissa128(
            ulong mantissa, int decimalShift, out ulong hi, out ulong lo)
        {
            hi = 0;
            lo = mantissa;
            while (decimalShift > 0)
            {
                var chunk = Math.Min(decimalShift, ScaleDigits);
                var factor = Pow10[chunk];
                Int128Math.Mul64(lo, factor, out var carry, out var scaledLo);
                hi = unchecked(hi * factor + carry);
                lo = scaledLo;
                decimalShift -= chunk;
            }
        }

        /// <summary>Compares values, returning a total order.</summary>
        public int CompareTo(BigNum other)
        {
            var signComparison = Sign.CompareTo(other.Sign);
            if (signComparison != 0 || IsZero)
            {
                return signComparison;
            }

            var magnitude = (long)CountDigits64((ulong)Math.Abs(Mantissa)) + Exponent - 1;
            var otherMagnitude = (long)CountDigits64((ulong)Math.Abs(other.Mantissa))
                                 + other.Exponent - 1;
            var magnitudeComparison = magnitude.CompareTo(otherMagnitude);
            if (magnitudeComparison != 0)
            {
                return Sign > 0 ? magnitudeComparison : -magnitudeComparison;
            }

            var exponent = Math.Min(Exponent, other.Exponent);
            ScaleMantissa128(
                (ulong)Math.Abs(Mantissa), Exponent - exponent, out var hi, out var lo);
            ScaleMantissa128(
                (ulong)Math.Abs(other.Mantissa), other.Exponent - exponent,
                out var otherHi, out var otherLo);
            var alignedComparison = Compare128(hi, lo, otherHi, otherLo);
            return Sign > 0 ? alignedComparison : -alignedComparison;
        }

        /// <summary>Canonical-form field comparison — equal values always share identical bits.</summary>
        public bool Equals(BigNum other) => Mantissa == other.Mantissa && Exponent == other.Exponent;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is BigNum other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)2_166_136_261u;
                hash = (hash ^ (int)Mantissa) * 16_777_619;
                hash = (hash ^ (int)(Mantissa >> 32)) * 16_777_619;
                hash = (hash ^ Exponent) * 16_777_619;
                return hash;
            }
        }

        /// <summary>Equality.</summary>
        public static bool operator ==(BigNum a, BigNum b) => a.Equals(b);

        /// <summary>Inequality.</summary>
        public static bool operator !=(BigNum a, BigNum b) => !a.Equals(b);

        /// <summary>Less than.</summary>
        public static bool operator <(BigNum a, BigNum b) => a.CompareTo(b) < 0;

        /// <summary>Less than or equal.</summary>
        public static bool operator <=(BigNum a, BigNum b) => a.CompareTo(b) <= 0;

        /// <summary>Greater than.</summary>
        public static bool operator >(BigNum a, BigNum b) => a.CompareTo(b) > 0;

        /// <summary>Greater than or equal.</summary>
        public static bool operator >=(BigNum a, BigNum b) => a.CompareTo(b) >= 0;

        /// <summary>Debug representation. Not for hot paths — use <see cref="TryFormat"/> for display.</summary>
        public override string ToString() =>
            Exponent == 0
                ? Mantissa.ToString(CultureInfo.InvariantCulture)
                : Mantissa.ToString(CultureInfo.InvariantCulture) + "e"
                  + Exponent.ToString(CultureInfo.InvariantCulture);
    }
}
