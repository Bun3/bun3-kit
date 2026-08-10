using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// 결정론적 십진 대수: 값 = Mantissa × 10^Exponent. 정수 연산만 사용하므로 플랫폼
    /// 무관하게 비트 동일 결과를 낸다. 유효 18~19자리 — long 범위(±9.2×10^18)까지 정수
    /// 정확, 그 너머는 근사(하위 자릿수 절사, 0 방향).
    /// 정규 형식: Mantissa==0이면 Exponent==0, 그 외 Mantissa는 10의 배수가 아니다 —
    /// 같은 값은 항상 같은 비트라 동등성·해시가 필드 비교로 끝난다.
    /// </summary>
    public readonly struct BigNum : IEquatable<BigNum>, IComparable<BigNum>
    {
        /// <summary>지수 한계. 초과는 <see cref="BigNumOverflowException"/>, 미만(언더플로)은 0으로 수렴.</summary>
        public const int MaxExponent = 100_000_000;

        private const long LongMaxDiv10 = long.MaxValue / 10;          //  922337203685477580

        /// <summary>가수. 정규 형식에서 10의 배수가 아니다(0 제외).</summary>
        public readonly long Mantissa;

        /// <summary>십진 지수.</summary>
        public readonly int Exponent;

        /// <summary>0.</summary>
        public static readonly BigNum Zero = default;

        /// <summary>1.</summary>
        public static readonly BigNum One = new BigNum(1, 0);

        private BigNum(long mantissa, int exponent)
        {
            Mantissa = mantissa;
            Exponent = exponent;
        }

        /// <summary>가수×10^지수로 값을 만든다. 정규화하며, 지수 한계 초과 시 던진다.</summary>
        public static BigNum FromParts(long mantissa, int exponent) =>
            Canonicalize(mantissa, exponent);

        /// <summary>long 정수는 정확하게 변환된다.</summary>
        public static implicit operator BigNum(long value) => Canonicalize(value, 0);

        /// <summary>int 정수는 정확하게 변환된다.</summary>
        public static implicit operator BigNum(int value) => Canonicalize(value, 0);

        /// <summary>값이 0인지 여부.</summary>
        public bool IsZero => Mantissa == 0;

        /// <summary>부호: -1, 0, +1.</summary>
        public int Sign => Math.Sign(Mantissa);

        private static BigNum Canonicalize(long mantissa, long exponent)
        {
            if (mantissa == 0)
            {
                return default;
            }

            if (mantissa == long.MinValue)
            {
                // 절댓값 부정이 불가능한 유일한 값 — 한 자리 내려 정규화 경로에 합류
                mantissa /= 10;
                exponent++;
            }

            while (mantissa % 10 == 0)
            {
                mantissa /= 10;
                exponent++;
            }

            if (exponent > MaxExponent)
            {
                throw new BigNumOverflowException(exponent);
            }

            if (exponent < -MaxExponent)
            {
                return default;   // 언더플로 — 극소값은 0으로 수렴(정보 손실이 자연스러운 방향)
            }

            return new BigNum(mantissa, (int)exponent);
        }

        /// <summary>덧셈. 유효 자릿수 밖의 항은 절사된다(0 방향).</summary>
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

            if (a.Exponent < b.Exponent)
            {
                (a, b) = (b, a);   // a가 큰 지수
            }

            long am = a.Mantissa;
            long ae = a.Exponent;
            long bm = b.Mantissa;
            long be = b.Exponent;

            // a 가수를 키워 지수를 b에 근접 — 정밀도 보존
            while (ae > be && am > -LongMaxDiv10 && am < LongMaxDiv10)
            {
                am *= 10;
                ae--;
            }

            var gap = ae - be;
            if (gap > 18)
            {
                return a;   // b는 유효 자릿수 창 밖
            }

            // 남은 갭은 b를 절사해 올린다
            for (var i = 0L; i < gap; i++)
            {
                bm /= 10;
            }

            // 실제 오버플로가 날 때만 한 자리 양보 (같은 지수 정렬 유지) — long 범위 안의
            // 합은 항상 정확하다(스펙 §6: 9.2e18까지 정수 정확).
            var sum = unchecked(am + bm);
            if (((am ^ sum) & (bm ^ sum)) < 0)   // 같은 부호 피연산자 합의 부호 반전 = 오버플로
            {
                am /= 10;
                bm /= 10;
                ae++;
                sum = am + bm;   // |가수| ≤ 9.3e17 — 재오버플로 불가
            }

            return Canonicalize(sum, ae);
        }

        /// <summary>뺄셈.</summary>
        public static BigNum operator -(BigNum a, BigNum b) => a + (-b);

        /// <summary>부호 반전.</summary>
        public static BigNum operator -(BigNum value) =>
            value.IsZero ? value : new BigNum(-value.Mantissa, value.Exponent);

        private const ulong TenPow18 = 1_000_000_000_000_000_000UL;

        /// <summary>곱셈. 결과는 유효 18~19자리로 절사(0 방향)된다.</summary>
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

        /// <summary>나눗셈. 결과는 유효 17~19자리로 절사(0 방향)된다. 0으로 나누면 던진다.</summary>
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
            var exponent = (long)a.Exponent - b.Exponent - 18;

            // 두 가수를 [10^18, 10^19) 구간으로 정규화 — 고정폭 스케일링은 소가수/대가수
            // 조합에서 유효 자릿수가 붕괴하고, 분모 가수가 분자×10^18보다 크면 0으로 무너진다.
            while (ua < TenPow18)
            {
                ua *= 10;
                exponent--;
            }

            while (ub < TenPow18)
            {
                ub *= 10;
                exponent++;
            }

            Int128Math.Mul64(ua, TenPow18, out var hi, out var lo);
            Int128Math.DivRem(hi, lo, ub, out var qHi, out var qLo, out _);
            var mantissa = ReduceToLong(qHi, qLo, ref exponent);
            return Canonicalize(negative ? -mantissa : mantissa, exponent);
        }

        // 128비트 값을 long 범위(≤ long.MaxValue)까지 10^k 절사로 줄인다. exponent에 k를 더한다.
        // 반드시 한 자리씩 줄인다 — 큰 폭 점프는 필요 이상으로 깎아 유효 자릿수를 파괴한다.
        // (10^18 점프 최적화가 실제로 그 버그였다. 성능이 병목으로 측정되면 자릿수 계산
        // 기반 최소 시프트로 교체하되, 절사 폭이 "필요한 만큼만"이라는 불변식을 지킬 것.)
        private static long ReduceToLong(ulong hi, ulong lo, ref long exponent)
        {
            while (hi != 0 || lo > long.MaxValue)
            {
                Int128Math.DivRem(hi, lo, 10, out hi, out lo, out _);
                exponent++;
            }

            return (long)lo;
        }

        /// <summary>값 비교. 유효 자릿수 밖 차이는 같음으로 본다(정밀도 계약과 일관) —
        /// Equals(비트 동등)와 판정이 다를 수 있으므로 정렬 컨테이너의 키로 쓰지 말 것.</summary>
        public int CompareTo(BigNum other) => (this - other).Sign;

        /// <summary>정규 형식 필드 비교 — 같은 값은 항상 같은 비트다.</summary>
        public bool Equals(BigNum other) => Mantissa == other.Mantissa && Exponent == other.Exponent;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is BigNum other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Mantissa, Exponent);

        /// <summary>동등 비교.</summary>
        public static bool operator ==(BigNum a, BigNum b) => a.Equals(b);

        /// <summary>비동등 비교.</summary>
        public static bool operator !=(BigNum a, BigNum b) => !a.Equals(b);

        /// <summary>미만.</summary>
        public static bool operator <(BigNum a, BigNum b) => a.CompareTo(b) < 0;

        /// <summary>이하.</summary>
        public static bool operator <=(BigNum a, BigNum b) => a.CompareTo(b) <= 0;

        /// <summary>초과.</summary>
        public static bool operator >(BigNum a, BigNum b) => a.CompareTo(b) > 0;

        /// <summary>이상.</summary>
        public static bool operator >=(BigNum a, BigNum b) => a.CompareTo(b) >= 0;

        /// <summary>
        /// 무할당 표시 포맷. 단위 테이블 안이면 "1.5만"/"3.45B" 형태(소수 최대 2자리,
        /// 트레일링 0 제거), 테이블을 넘으면 "1.23e45" 지수 표기. format이 null이면
        /// <see cref="BigNumFormat.Alpha"/>. 버퍼가 부족하면 false.
        /// </summary>
        public bool TryFormat(Span<char> destination, out int charsWritten, BigNumFormat? format = null)
        {
            format ??= BigNumFormat.Alpha;
            charsWritten = 0;

            if (IsZero)
            {
                return TryAppendChar(destination, ref charsWritten, '0');
            }

            var negative = Mantissa < 0;
            var absMantissa = (ulong)Math.Abs(Mantissa);
            var digitCount = CountDigits(absMantissa);
            var magnitude = (long)Exponent + digitCount - 1;   // 최상위 자리의 십진 지수

            if (negative && !TryAppendChar(destination, ref charsWritten, '-'))
            {
                return false;
            }

            // 1) 그룹 미만의 작은 값: 자릿수 그대로 (소수 포함, magnitude ≥ -2까지)
            if (magnitude < format.GroupDigits && Exponent >= -18 && magnitude >= -2)
            {
                return TryWritePlain(destination, ref charsWritten, absMantissa, Exponent);
            }

            // 2) 단위 테이블 범위: 선두부를 단위로 나눠 쓴다
            var unitIndex = magnitude >= 0 ? (int)(magnitude / format.GroupDigits) : -1;
            if (unitIndex >= 1 && unitIndex < format.Units.Length)
            {
                var integerDigits = (int)(magnitude - (long)unitIndex * format.GroupDigits) + 1;
                return TryWriteScaled(destination, ref charsWritten, absMantissa, digitCount,
                           integerDigits)
                       && TryAppendString(destination, ref charsWritten, format.Units[unitIndex]);
            }

            // 3) 폴백: 지수 표기 m.mm'e'[-]EEE
            if (!TryWriteScaled(destination, ref charsWritten, absMantissa, digitCount, 1)
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

        private static int CountDigits(ulong value)
        {
            var digits = 1;
            while (value >= 10)
            {
                value /= 10;
                digits++;
            }

            return digits;
        }

        // 정수/소수 그대로: mantissa × 10^exponent (exponent ≤ 0 구간 전용)
        private static bool TryWritePlain(
            Span<char> destination, ref int written, ulong mantissa, int exponent)
        {
            if (exponent >= 0)
            {
                if (!TryAppendUInt(destination, ref written, mantissa))
                {
                    return false;
                }

                for (var i = 0; i < exponent; i++)
                {
                    if (!TryAppendChar(destination, ref written, '0'))
                    {
                        return false;
                    }
                }

                return true;
            }

            var fracDigits = -exponent;
            var divisor = 1UL;
            for (var i = 0; i < fracDigits; i++)
            {
                divisor *= 10;
            }

            var integerPart = mantissa / divisor;
            var fraction = mantissa % divisor;

            // 소수부 최대 2자리 절사(스펙 §6) — 그 후 트레일링 0 제거
            while (fracDigits > 2)
            {
                fraction /= 10;
                fracDigits--;
            }

            while (fraction != 0 && fraction % 10 == 0)
            {
                fraction /= 10;
                fracDigits--;
            }

            if (!TryAppendUInt(destination, ref written, integerPart))
            {
                return false;
            }

            if (fraction == 0)
            {
                return true;   // 절사/제거 후 소수부 없음 — 정수만
            }

            if (!TryAppendChar(destination, ref written, '.'))
            {
                return false;
            }

            Span<char> frac = stackalloc char[20];
            var f = fracDigits;
            for (var i = 0; i < fracDigits; i++)
            {
                frac[--f] = (char)('0' + (int)(fraction % 10));
                fraction /= 10;
            }

            for (var i = 0; i < fracDigits; i++)
            {
                if (!TryAppendChar(destination, ref written, frac[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // 가수의 선두 integerDigits 자리를 정수부로, 이어 최대 2자리 소수부(절사, 0 제거)
        private static bool TryWriteScaled(
            Span<char> destination, ref int written, ulong mantissa, int digitCount, int integerDigits)
        {
            // 정수부 자릿수가 가수 자릿수보다 많으면 0 패딩 (예: 가수 92, 정수부 3자리 → "920")
            if (integerDigits >= digitCount)
            {
                if (!TryAppendUInt(destination, ref written, mantissa))
                {
                    return false;
                }

                for (var i = 0; i < integerDigits - digitCount; i++)
                {
                    if (!TryAppendChar(destination, ref written, '0'))
                    {
                        return false;
                    }
                }

                return true;
            }

            // 정수부 뒤 소수 2자리까지만 남기고 절사
            var keep = integerDigits + 2;
            var drop = digitCount - keep;
            for (var i = 0; i < drop; i++)
            {
                mantissa /= 10;
            }

            var scale = 1UL;
            var fracLen = Math.Min(2, Math.Max(0, digitCount - integerDigits));
            for (var i = 0; i < fracLen; i++)
            {
                scale *= 10;
            }

            var integerPart = mantissa / scale;
            var fraction = mantissa % scale;

            while (fraction != 0 && fraction % 10 == 0)
            {
                fraction /= 10;
                fracLen--;
            }

            if (!TryAppendUInt(destination, ref written, integerPart))
            {
                return false;
            }

            if (fraction == 0)
            {
                return true;
            }

            if (!TryAppendChar(destination, ref written, '.'))
            {
                return false;
            }

            Span<char> frac = stackalloc char[4];
            var f = fracLen;
            for (var i = 0; i < fracLen; i++)
            {
                frac[--f] = (char)('0' + (int)(fraction % 10));
                fraction /= 10;
            }

            for (var i = 0; i < fracLen; i++)
            {
                if (!TryAppendChar(destination, ref written, frac[i]))
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

        /// <summary>디버그 표기. 핫패스 사용 금지 — 표시용은 TryFormat(Task 5).</summary>
        public override string ToString() =>
            Exponent == 0 ? Mantissa.ToString() : $"{Mantissa}e{Exponent}";
    }
}
