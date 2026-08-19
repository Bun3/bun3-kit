#nullable enable
using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>BigNum partial — display formatting and parsing, separate from the math core.</summary>
    public readonly partial struct BigNum
    {
        /// <summary>
        /// Parses a string into a BigNum. Invariant-only grammar: <c>-?\d+(\.\d+)?([eE][+-]?\d+)?</c>.
        /// Integer arithmetic only (no float/double round-trip). Significant digits are absorbed
        /// into the mantissa up to the long-safe range (<see cref="long.MaxValue"/>); excess integer
        /// digits raise the exponent to preserve place value, excess fraction digits truncate toward
        /// zero (the exponent has already dropped only by the absorbed digits). The result is
        /// canonicalized via <see cref="FromParts"/>. Leading zeros (before the integer part, or
        /// before the first significant fraction digit) contribute nothing.
        /// </summary>
        /// <param name="text">Text to parse.</param>
        /// <param name="value">Parsed value on success; <see cref="Zero"/> on failure.</param>
        /// <returns><see langword="true"/> on success; <see langword="false"/> when the grammar does
        /// not match or the exponent exceeds <see cref="MaxExponent"/> (never throws).</returns>
        public static bool TryParse(ReadOnlySpan<char> text, out BigNum value)
        {
            value = default;
            var length = text.Length;
            if (length == 0)
            {
                return false;
            }

            var i = 0;
            var negative = text[0] == '-';
            if (negative)
            {
                i = 1;
            }

            var mantissa = 0L;
            var exponent = 0L;
            var seenNonZero = false;

            var integerStart = i;
            while (i < length && IsAsciiDigit(text[i]))
            {
                AbsorbIntegerDigit(text[i] - '0', ref mantissa, ref exponent, ref seenNonZero);
                i++;
            }

            if (i == integerStart)
            {
                return false;   // integer part requires at least one digit — bare sign or non-digit
            }

            if (i < length && text[i] == '.')
            {
                i++;
                var fractionStart = i;
                while (i < length && IsAsciiDigit(text[i]))
                {
                    AbsorbFractionDigit(text[i] - '0', ref mantissa, ref exponent, ref seenNonZero);
                    i++;
                }

                if (i == fractionStart)
                {
                    return false;   // at least one digit required after the decimal point
                }
            }

            if (i < length && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                var exponentNegative = false;
                if (i < length && (text[i] == '+' || text[i] == '-'))
                {
                    exponentNegative = text[i] == '-';
                    i++;
                }

                var exponentDigitStart = i;
                var explicitExponent = 0L;
                while (i < length && IsAsciiDigit(text[i]))
                {
                    // Past MaxExponent failure is already certain — stop accumulating (overflow guard).
                    if (explicitExponent <= MaxExponent)
                    {
                        explicitExponent = explicitExponent * 10 + (text[i] - '0');
                    }

                    i++;
                }

                if (i == exponentDigitStart)
                {
                    return false;   // exponent part requires at least one digit
                }

                exponent += exponentNegative ? -explicitExponent : explicitExponent;
            }

            if (i != length)
            {
                return false;   // trailing characters outside the grammar
            }

            if (exponent > MaxExponent)
            {
                return false;   // reject before FromParts throws BigNumOverflowException
            }

            value = FromParts(negative ? -mantissa : mantissa, (int)exponent);
            return true;
        }

        // Absorb one integer digit: skip leading zeros; accumulate while within the long-safe
        // range, otherwise raise the exponent to preserve place value.
        private static void AbsorbIntegerDigit(int digit, ref long mantissa, ref long exponent, ref bool seenNonZero)
        {
            if (digit == 0 && !seenNonZero)
            {
                return;
            }

            seenNonZero = true;
            if (mantissa <= (long.MaxValue - digit) / 10)
            {
                mantissa = mantissa * 10 + digit;
            }
            else
            {
                exponent++;
            }
        }

        // Absorb one fraction digit: leading zeros before the first significant digit still lower
        // the exponent. Digits beyond capacity truncate toward zero (the exponent already accounts
        // only for absorbed digits — no further adjustment).
        private static void AbsorbFractionDigit(int digit, ref long mantissa, ref long exponent, ref bool seenNonZero)
        {
            if (digit == 0 && !seenNonZero)
            {
                exponent--;
                return;
            }

            seenNonZero = true;
            if (mantissa <= (long.MaxValue - digit) / 10)
            {
                mantissa = mantissa * 10 + digit;
                exponent--;
            }
        }

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

        /// <summary>Builds the display string with a 256-char budget. Allocates a string; for
        /// per-frame UI hot paths use a caller-owned buffer with <see cref="TryFormat"/>.</summary>
        /// <param name="format">Display format; <see cref="BigNumFormat.Base"/> when <see langword="null"/>.</param>
        /// <returns>The formatted display string.</returns>
        /// <exception cref="InvalidOperationException">When the result exceeds the default 256-char budget.</exception>
        public string ToDisplayString(BigNumFormat? format = null) =>
            ToDisplayString(format, 256);

        /// <summary>Builds the display string with the given length budget. Allocates the string
        /// plus, beyond 128 chars, a temporary array up to <paramref name="maxLength"/>.</summary>
        /// <param name="format">Display format; <see cref="BigNumFormat.Base"/> when <see langword="null"/>.</param>
        /// <param name="maxLength">Maximum number of display characters allowed.</param>
        /// <returns>The formatted display string.</returns>
        /// <exception cref="ArgumentOutOfRangeException">When <paramref name="maxLength"/> is less than 1.</exception>
        /// <exception cref="InvalidOperationException">When the result exceeds the <paramref name="maxLength"/> budget.</exception>
        public string ToDisplayString(BigNumFormat? format, int maxLength)
        {
            if (maxLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLength));
            }

            Span<char> initial = stackalloc char[128];
            var first = maxLength < initial.Length ? initial.Slice(0, maxLength) : initial;
            if (TryFormat(first, out var written, format))
            {
                return new string(first.Slice(0, written));
            }

            if (maxLength <= initial.Length)
            {
                throw CreateDisplayBudgetException(maxLength);
            }

            var buffer = new char[maxLength];
            if (TryFormat(buffer, out written, format))
            {
                return new string(buffer, 0, written);
            }

            throw CreateDisplayBudgetException(maxLength);
        }

        private static InvalidOperationException CreateDisplayBudgetException(int maxLength) =>
            new InvalidOperationException(
                $"BigNum display output exceeded the {maxLength}-char budget. "
                + "Use BigNumOverflowStyle.Scientific, a larger maxLength, "
                + "or call TryFormat with a caller-owned buffer.");

        /// <summary>
        /// Allocation-free display formatting. Within the unit cap (MaxUnits): unit notation such
        /// as "1.5K"/"3.45B". Beyond the cap, per OverflowStyle: scientific ("1.23e45") or top unit
        /// retained ("12,345M"). Fraction digits, fixed fraction, and integer group separator follow
        /// the format settings. Null format means <see cref="BigNumFormat.Base"/>. Returns false when
        /// the buffer is too small.
        /// </summary>
        public bool TryFormat(Span<char> destination, out int charsWritten, BigNumFormat? format = null)
        {
            format ??= BigNumFormat.Base;
            charsWritten = 0;

            if (IsZero)
            {
                return TryAppendChar(destination, ref charsWritten, '0');
            }

            var negative = Mantissa < 0;
            var absMantissa = (ulong)Math.Abs(Mantissa);
            var digitCount = CountDigits64(absMantissa);
            var magnitude = (long)Exponent + digitCount - 1;   // decimal exponent of the most significant digit

            if (negative && !TryAppendChar(destination, ref charsWritten, '-'))
            {
                return false;
            }

            // 1) Small values below one group: literal digits (fractions included, down to magnitude >= -2)
            if (magnitude < format.GroupDigits && Exponent >= -ScaleDigits && magnitude >= -2)
            {
                return TryWritePlain(destination, ref charsWritten, absMantissa, Exponent, format);
            }

            // 2) Unit notation: within the cap use the matching unit; beyond it, TopUnit style keeps the top unit (integer part grows)
            var unitIndex = magnitude >= 0 ? (int)(magnitude / format.GroupDigits) : -1;
            if (unitIndex >= 1)
            {
                var index = Math.Min(unitIndex, format.MaxUnits);
                if (index == unitIndex || format.OverflowStyle == BigNumOverflowStyle.TopUnit)
                {
                    var integerDigits = (int)(magnitude - (long)index * format.GroupDigits) + 1;
                    return TryWriteScaled(destination, ref charsWritten, absMantissa, digitCount,
                               integerDigits, format)
                           && TryAppendString(destination, ref charsWritten, format.GetUnit(index));
                }
            }

            // 3) Fallback: scientific m.mm'e'[-]EEE
            if (!TryWriteScaled(destination, ref charsWritten, absMantissa, digitCount, 1, format)
                || !TryAppendChar(destination, ref charsWritten, 'e'))
            {
                return false;
            }

            if (magnitude < 0)
            {
                if (!TryAppendChar(destination, ref charsWritten, '-'))
                {
                    return false;
                }

                return TryAppendUInt(destination, ref charsWritten, (ulong)(-magnitude));
            }

            return TryAppendUInt(destination, ref charsWritten, (ulong)magnitude);
        }

        // Literal integer/fraction: mantissa × 10^exponent (exponent <= 0 path only)
        private static bool TryWritePlain(
            Span<char> destination, ref int written, ulong mantissa, int exponent, BigNumFormat format)
        {
            if (exponent >= 0)
            {
                // Integer — mantissa digits plus exponent zeros (streamed with separators)
                return TryAppendGroupedInteger(
                    destination, ref written, mantissa,
                    CountDigits64(mantissa) + exponent, format.IntegerGroupSeparator);
            }

            var fracDigits = -exponent;
            var divisor = Pow10[fracDigits];
            var integerPart = mantissa / divisor;
            var fraction = mantissa % divisor;

            return TryAppendGroupedInteger(
                       destination, ref written, integerPart,
                       CountDigits64(integerPart), format.IntegerGroupSeparator)
                   && TryWriteFraction(destination, ref written, fraction, fracDigits, format);
        }

        // Leading integerDigits of the mantissa become the integer part, then the fraction
        // (truncated/padded per the format settings)
        private static bool TryWriteScaled(
            Span<char> destination, ref int written, ulong mantissa, int digitCount,
            int integerDigits, BigNumFormat format)
        {
            // Zero-pad when the integer part needs more digits than the mantissa has (e.g. mantissa 92, 3 integer digits -> "920")
            if (integerDigits >= digitCount)
            {
                return TryAppendGroupedInteger(
                           destination, ref written, mantissa, integerDigits, format.IntegerGroupSeparator)
                       && TryWriteFraction(destination, ref written, 0, 0, format);
            }

            // Keep at most MaxFractionDigits after the integer part; truncate the rest
            var fracLen = Math.Min(format.MaxFractionDigits, digitCount - integerDigits);
            var drop = digitCount - integerDigits - fracLen;
            mantissa /= Pow10[drop];

            var scale = Pow10[fracLen];
            var integerPart = mantissa / scale;
            var fraction = mantissa % scale;

            return TryAppendGroupedInteger(
                       destination, ref written, integerPart, integerDigits, format.IntegerGroupSeparator)
                   && TryWriteFraction(destination, ref written, fraction, fracLen, format);
        }

        // Writes the fraction. fraction holds fracDigits digits (leading zeros included). Truncate
        // to MaxFractionDigits; with Trim, drop trailing zeros (omit the point when empty),
        // otherwise zero-pad to exactly MaxFractionDigits.
        private static bool TryWriteFraction(
            Span<char> destination, ref int written, ulong fraction, int fracDigits, BigNumFormat format)
        {
            while (fracDigits > format.MaxFractionDigits)
            {
                fraction /= 10;
                fracDigits--;
            }

            if (format.TrimFractionZeros)
            {
                while (fraction != 0 && fraction % 10 == 0)
                {
                    fraction /= 10;
                    fracDigits--;
                }

                if (fraction == 0)
                {
                    return true;   // no fraction — omit the decimal point
                }
            }
            else if (format.MaxFractionDigits == 0)
            {
                return true;
            }

            if (!TryAppendChar(destination, ref written, '.'))
            {
                return false;
            }

            Span<char> digits = stackalloc char[12];
            var f = fracDigits;
            for (var i = 0; i < fracDigits; i++)
            {
                digits[--f] = (char)('0' + (int)(fraction % 10));
                fraction /= 10;
            }

            for (var i = 0; i < fracDigits; i++)
            {
                if (!TryAppendChar(destination, ref written, digits[i]))
                {
                    return false;
                }
            }

            // Fixed fraction: zero-pad the remaining places ("2.00M")
            if (!format.TrimFractionZeros)
            {
                for (var i = fracDigits; i < format.MaxFractionDigits; i++)
                {
                    if (!TryAppendChar(destination, ref written, '0'))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // Writes value's digits zero-padded to totalDigits; inserts separator every 3 digits from
        // the right when set. Streaming, so arbitrarily long integer parts stay allocation-free
        // (returns false when the buffer runs out).
        private static bool TryAppendGroupedInteger(
            Span<char> destination, ref int written, ulong value, int totalDigits, char? separator)
        {
            Span<char> lead = stackalloc char[MantissaMaxDigits + 1];
            var leadCount = 0;
            do
            {
                lead[leadCount++] = (char)('0' + (int)(value % 10));
                value /= 10;
            }
            while (value != 0);

            for (var i = 0; i < totalDigits; i++)
            {
                if (separator.HasValue && i > 0 && (totalDigits - i) % 3 == 0)
                {
                    if (!TryAppendChar(destination, ref written, separator.Value))
                    {
                        return false;
                    }
                }

                var c = i < leadCount ? lead[leadCount - 1 - i] : '0';
                if (!TryAppendChar(destination, ref written, c))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryAppendChar(Span<char> destination, ref int written, char c)
        {
            if (written >= destination.Length)
            {
                return false;
            }

            destination[written++] = c;
            return true;
        }

        private static bool TryAppendString(Span<char> destination, ref int written, string s)
        {
            foreach (var c in s)
            {
                if (!TryAppendChar(destination, ref written, c))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryAppendUInt(Span<char> destination, ref int written, ulong value)
        {
            Span<char> digits = stackalloc char[MantissaMaxDigits + 1];
            var count = 0;
            do
            {
                digits[count++] = (char)('0' + (int)(value % 10));
                value /= 10;
            }
            while (value != 0);

            for (var i = count - 1; i >= 0; i--)
            {
                if (!TryAppendChar(destination, ref written, digits[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
