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
        private const long HalfLongMax = long.MaxValue / 2;

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

            // 합이 long을 넘지 않도록 한 자리 양보 (같은 지수 정렬 유지)
            if (am > HalfLongMax || am < -HalfLongMax || bm > HalfLongMax || bm < -HalfLongMax)
            {
                am /= 10;
                bm /= 10;
                ae++;
            }

            return Canonicalize(am + bm, ae);
        }

        /// <summary>뺄셈.</summary>
        public static BigNum operator -(BigNum a, BigNum b) => a + (-b);

        /// <summary>부호 반전.</summary>
        public static BigNum operator -(BigNum value) =>
            value.IsZero ? value : new BigNum(-value.Mantissa, value.Exponent);

        /// <summary>값 비교. 유효 자릿수 밖 차이는 같음으로 본다(정밀도 계약과 일관).</summary>
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
