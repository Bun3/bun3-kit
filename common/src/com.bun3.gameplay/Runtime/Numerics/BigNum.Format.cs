using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>BigNum의 표시 포맷 파트 — 수학 코어(BigNum.cs)와 분리된 partial.</summary>
    public readonly partial struct BigNum
    {
        /// <summary>최대 256자 예산으로 표시 문자열을 생성한다. 문자열 힙 할당이 발생하므로
        /// 매 프레임 UI 핫패스에서는 caller-owned 버퍼와 <see cref="TryFormat"/>을 사용한다.</summary>
        /// <param name="format">표시 형식. <see langword="null"/>이면 <see cref="BigNumFormat.Base"/>.</param>
        /// <returns>지정한 형식의 표시 문자열.</returns>
        /// <exception cref="InvalidOperationException">표시 결과가 기본 256자 예산을 초과한 경우.</exception>
        public string ToDisplayString(BigNumFormat? format = null) =>
            ToDisplayString(format, 256);

        /// <summary>지정한 최대 길이 예산으로 표시 문자열을 생성한다. 문자열과, 128자를 넘는
        /// 경우 최대 <paramref name="maxLength"/> 크기의 임시 배열 힙 할당이 발생한다.</summary>
        /// <param name="format">표시 형식. <see langword="null"/>이면 <see cref="BigNumFormat.Base"/>.</param>
        /// <param name="maxLength">허용할 표시 문자열의 최대 문자 수.</param>
        /// <returns>지정한 형식의 표시 문자열.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/>가 1보다 작은 경우.</exception>
        /// <exception cref="InvalidOperationException">표시 결과가 <paramref name="maxLength"/> 예산을 초과한 경우.</exception>
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
                $"BigNum 표시 결과가 {maxLength}자 예산을 초과했습니다. "
                + "BigNumOverflowStyle.Scientific을 사용하거나 더 큰 maxLength를 지정하거나 "
                + "caller-owned 버퍼로 TryFormat을 호출하세요.");

        /// <summary>
        /// 무할당 표시 포맷. 단위 상한(MaxUnits) 안이면 "1.5만"/"3.45B" 형태, 상한을 넘으면
        /// OverflowStyle에 따라 지수 표기("1.23e45") 또는 상한 단위 유지("12,345M" —
        /// idlez ToUnitString 방식). 소수 자릿수·고정 소수·정수부 구분자는 format 설정을
        /// 따른다. format이 null이면 <see cref="BigNumFormat.Base"/>. 버퍼가 부족하면 false.
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
            var magnitude = (long)Exponent + digitCount - 1;   // 최상위 자리의 십진 지수

            if (negative && !TryAppendChar(destination, ref charsWritten, '-'))
            {
                return false;
            }

            // 1) 그룹 미만의 작은 값: 자릿수 그대로 (소수 포함, magnitude ≥ -2까지)
            if (magnitude < format.GroupDigits && Exponent >= -ScaleDigits && magnitude >= -2)
            {
                return TryWritePlain(destination, ref charsWritten, absMantissa, Exponent, format);
            }

            // 2) 단위 표기: 상한 내면 해당 단위, 초과 시 TopUnit 스타일이면 상한 단위 유지(정수부 성장)
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

            // 3) 폴백: 지수 표기 m.mm'e'[-]EEE
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

        // 정수/소수 그대로: mantissa × 10^exponent (exponent ≤ 0 구간 전용)
        private static bool TryWritePlain(
            Span<char> destination, ref int written, ulong mantissa, int exponent, BigNumFormat format)
        {
            if (exponent >= 0)
            {
                // 정수 — 가수 자릿수 + 지수만큼의 0 (구분자 포함 스트리밍)
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

        // 가수의 선두 integerDigits 자리를 정수부로, 이어 소수부(format 설정에 따라 절사/패딩)
        private static bool TryWriteScaled(
            Span<char> destination, ref int written, ulong mantissa, int digitCount,
            int integerDigits, BigNumFormat format)
        {
            // 정수부 자릿수가 가수 자릿수 이상이면 0 패딩 (예: 가수 92, 정수부 3자리 → "920")
            if (integerDigits >= digitCount)
            {
                return TryAppendGroupedInteger(
                           destination, ref written, mantissa, integerDigits, format.IntegerGroupSeparator)
                       && TryWriteFraction(destination, ref written, 0, 0, format);
            }

            // 정수부 뒤 소수 상한 자릿수까지만 남기고 절사
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

        // 소수부 쓰기. fraction은 fracDigits 자리(선행 0 포함) 값. MaxFractionDigits로 절사한 뒤
        // Trim이면 트레일링 0 제거(비면 소수점 생략), 아니면 정확히 MaxFractionDigits 자리로 0 패딩.
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
                    return true;   // 소수부 없음 — 소수점 생략
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

            // 고정 소수: 남은 자리를 0으로 채운다 ("2.00M")
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

        // value의 자릿수 + 0 패딩으로 totalDigits 자리 정수부를 쓴다. separator가 있으면
        // 오른쪽에서 3자리마다 끼운다. 스트리밍이라 정수부가 아무리 길어도 무할당
        // (버퍼가 모자라면 false로 끝난다).
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
