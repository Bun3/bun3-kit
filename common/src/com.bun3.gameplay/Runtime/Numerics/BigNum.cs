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
    public readonly partial struct BigNum : IEquatable<BigNum>, IComparable<BigNum>
    {
        /// <summary>지수 한계. 초과는 <see cref="BigNumOverflowException"/>, 미만(언더플로)은 0으로 수렴.</summary>
        public const int MaxExponent = 100_000_000;

        // 가수(long)가 담는 최대 십진 유효 자릿수. 정수 정확 한계(±9.2×10^18)의 근원.
        private const int MantissaMaxDigits = 19;

        // 내부 스케일 자릿수(= 10^18) — 나눗셈 분자 확장 폭이자 덧셈 정렬 창.
        private const int ScaleDigits = MantissaMaxDigits - 1;

        // 128비트 값의 최대 십진 자릿수 — 자릿수 테이블 크기.
        private const int MaxDigits128 = 39;

        // 10^0 .. 10^MantissaMaxDigits (10^19까지 ulong에 든다)
        private static readonly ulong[] Pow10 = BuildPow10();

        // long.MaxValue / 10^k (k = 0..18) — "×10^k가 long을 안 넘는가" 경계표
        private static readonly long[] LongMaxDivPow10 = BuildLongMaxDivPow10();

        private static long[] BuildLongMaxDivPow10()
        {
            var table = new long[MantissaMaxDigits];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = (long)((ulong)long.MaxValue / Pow10[i]);
            }

            return table;
        }

        // 10^i의 128비트 표현 (i = 0..38) — 128비트 값의 자릿수 계산용
        private static readonly ulong[] Pow10Hi128 = new ulong[MaxDigits128];
        private static readonly ulong[] Pow10Lo128 = new ulong[MaxDigits128];

        static BigNum()
        {
            Pow10Lo128[0] = 1;
            for (var i = 1; i < MaxDigits128; i++)
            {
                // (hi:lo) × 10 — lo 곱의 자리올림을 hi에 편입
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
            // 이진 트리 4~5비교 — 선형 스캔(최대 19비교) 대비 핫패스 절감
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

            // hi != 0 ⇒ 값 ≥ 2^64 > 10^19 ⇒ 자릿수 ∈ [20, 39]. "값 < 10^d"인 최소 d를 이진 탐색.
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

        // double 변환 정규화 구간 [1e15, 1e16) — 유효 16자리 확보
        private const double DoubleNormalizeLow = 1e15;
        private const double DoubleNormalizeHigh = 1e16;

        /// <summary>double을 절사 변환한다(유효 약 16자리) — **명시적**: 손실 변환이며,
        /// 런타임 부동소수 값을 심에 무심코 흘리는 실수를 캐스트가 막는다(결정론 경계).
        /// 데이터 로드 등 경계에서 1회 변환하는 용도. NaN/무한대는 던진다.
        /// 변환 자체는 IEEE 기본 연산(×10/÷10)만 사용해 같은 입력 비트면 어디서나 같은 결과다.</summary>
        public static explicit operator BigNum(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("NaN/무한대는 BigNum으로 변환할 수 없다.", nameof(value));
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

            var mantissa = (long)abs;   // 절사 (0 방향)
            return Canonicalize(negative ? -mantissa : mantissa, exponent);
        }

        /// <summary>float을 절사 변환한다(유효 약 7자리) — 명시적. 규칙은 double과 동일.</summary>
        public static explicit operator BigNum(float value) => (BigNum)(double)value;

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

            if (mantissa % 10 == 0)
            {
                // 트레일링 0 제거 — 8→4→2→1 사다리로 나눗셈 횟수 절감 (결과는 순서 무관 동일)
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

            // a 가수를 키워 지수를 b에 근접 — 정밀도 보존. 자릿수 기반 일괄 ×10^k
            // (한 자리씩 곱하는 루프와 결과 비트 동일: 경계 M-7..M에 10의 배수가 없어
            // "곱해도 long.MaxValue 이하" 판정이 루프 조건과 일치한다).
            var diff = ae - be;
            if (diff > 0)
            {
                var abs = am < 0 ? -am : am;   // 정규형 보장으로 long.MinValue 불가
                var k = MantissaMaxDigits - CountDigits64((ulong)abs);
                if (k > diff)
                {
                    k = (int)diff;
                }

                while (k > 0 && abs > LongMaxDivPow10[k])
                {
                    k--;   // 자릿수 추정의 경계 보정 — 최대 1회
                }

                am *= (long)Pow10[k];
                ae -= k;
            }

            var gap = ae - be;
            if (gap > ScaleDigits)
            {
                return a;   // b는 유효 자릿수 창 밖
            }

            // 남은 갭은 b를 절사해 올린다 (10^gap 일괄 나눗셈 = /10 gap회 합성과 동일)
            bm /= (long)Pow10[gap];

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

        // = Pow10[ScaleDigits] — const 문맥에서 배열 참조가 불가해 리터럴로 유지
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
            var exponent = (long)a.Exponent - b.Exponent - ScaleDigits;

            // 두 가수를 [10^18, 10^19) 구간으로 정규화 — 고정폭 스케일링은 소가수/대가수
            // 조합에서 유효 자릿수가 붕괴하고, 분모 가수가 분자×10^18보다 크면 0으로 무너진다.
            // 자릿수 기반 일괄 스케일: ×10^k 1회 (루프 반복과 결과 동일).
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

        // 128비트 값을 long 범위(≤ long.MaxValue)까지 10^k 절사로 줄인다. exponent에 k를 더한다.
        // 자릿수를 세서 "필요한 만큼만" 한 번에 절사한다 — 10^k 일괄 나눗셈은 /10 k회의
        // 합성과 절사 결과가 비트 동일하다(정수 나눗셈의 합성 법칙). 필요 이상 깎으면
        // 유효 자릿수가 파괴되므로(과거 10^18 고정 점프 버그) k는 자릿수 기반으로만 계산한다.
        private static long ReduceToLong(ulong hi, ulong lo, ref long exponent)
        {
            if (hi == 0 && lo <= long.MaxValue)
            {
                return (long)lo;   // 축소 불필요 — 일반 게임플레이 대역의 fast path
            }

            var digits = CountDigits128(hi, lo);
            if (digits > MantissaMaxDigits)
            {
                var k = Math.Min(digits - MantissaMaxDigits, MantissaMaxDigits);   // Pow10 테이블 상한(10^19) 방어
                Int128Math.DivRem(hi, lo, Pow10[k], out hi, out lo, out _);
                exponent += k;
            }

            // 19자리 몫은 long.MaxValue(9.22e18)를 넘을 수 있다 — 최대 1~2회 보정
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

        /// <summary>디버그 표기. 핫패스 사용 금지 — 표시용은 TryFormat(Task 5).</summary>
        public override string ToString() =>
            Exponent == 0 ? Mantissa.ToString() : $"{Mantissa}e{Exponent}";
    }
}
