#nullable enable
using System;
using System.Globalization;

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

        /// <summary>표현 가능한 최소값. <c>-long.MaxValue × 10^MaxExponent</c>.</summary>
        public static readonly BigNum MinValue = new BigNum(-long.MaxValue, MaxExponent);

        /// <summary>표현 가능한 최대값. <c>long.MaxValue × 10^MaxExponent</c>.</summary>
        public static readonly BigNum MaxValue = new BigNum(long.MaxValue, MaxExponent);

        private BigNum(long mantissa, int exponent)
        {
            Mantissa = mantissa;
            Exponent = exponent;
        }

        /// <summary>가수×10^지수로 값을 만든다. 정규화하며, 지수 한계 초과 시 던진다.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="mantissa"/>가 대칭 가수 범위 밖인 <see cref="long.MinValue"/>인 경우.
        /// </exception>
        public static BigNum FromParts(long mantissa, int exponent) =>
            Canonicalize(mantissa, exponent);

        /// <summary>long 정수는 정확하게 변환된다.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="value"/>가 대칭 가수 범위 밖인 <see cref="long.MinValue"/>인 경우.
        /// </exception>
        public static implicit operator BigNum(long value) => Canonicalize(value, 0);

        /// <summary>int 정수는 정확하게 변환된다.</summary>
        public static implicit operator BigNum(int value) => Canonicalize(value, 0);

        // double 변환 정규화 구간 [1e15, 1e16) — 유효 16자리 확보
        private const double DoubleNormalizeLow = 1e15;
        private const double DoubleNormalizeHigh = 1e16;

        private const float FloatNormalizeLow = 1e6f;
        private const float FloatNormalizeHigh = 1e7f;

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
                // 절댓값 부정이 불가능한 유일한 값 — 대칭 가수 범위 밖이므로 즉시 거부
                throw new ArgumentOutOfRangeException(
                    nameof(mantissa), mantissa,
                    "BigNum 가수는 -long.MaxValue 이상이어야 합니다.");
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

        /// <summary>
        /// 덧셈. 보존 범위 밖의 같은 부호 항은 무시하고, 반대 부호 항은 borrow를 반영한 뒤
        /// 0 방향으로 절사한다.
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

        /// <summary>값을 직접 비교하여 전체 순서를 반환한다.</summary>
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

        /// <summary>정규 형식 필드 비교 — 같은 값은 항상 같은 비트다.</summary>
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
            Exponent == 0
                ? Mantissa.ToString(CultureInfo.InvariantCulture)
                : Mantissa.ToString(CultureInfo.InvariantCulture) + "e"
                  + Exponent.ToString(CultureInfo.InvariantCulture);
    }
}
