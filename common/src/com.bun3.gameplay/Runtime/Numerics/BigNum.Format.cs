using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>BigNum의 표시 포맷 파트 — 수학 코어(BigNum.cs)와 분리된 partial.</summary>
    public readonly partial struct BigNum
    {
        /// <summary>표시 문자열을 생성한다 — **힙 할당 발생**. 매 프레임 갱신되는 UI 핫패스에선
        /// <see cref="TryFormat"/>(+ZString/TMP SetText 합성)를 쓰고, 이 메서드는 저빈도
        /// 경로(로그·텍스트 조립 등)에서만 쓸 것.</summary>
        public string ToDisplayString(BigNumFormat? format = null)
        {
            Span<char> buffer = stackalloc char[128];
            if (TryFormat(buffer, out var written, format))
            {
                return new string(buffer.Slice(0, written));
            }

            // 128자를 넘는 표기(TopUnit 대형 정수부) — 필요한 만큼 키워 재시도
            for (var size = 512; ; size *= 4)
            {
                var grown = new char[size];
                if (TryFormat(grown, out written, format))
                {
                    return new string(grown, 0, written);
                }
            }
        }

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
            if (magnitude < format.GroupDigits && Exponent >= -18 && magnitude >= -2)
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
                           && TryAppendString(destination, ref charsWritten, format.Units[index]);
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
            Span<char> lead = stackalloc char[20];
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
            Span<char> digits = stackalloc char[20];
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
