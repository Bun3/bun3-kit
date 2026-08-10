using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// BigNum이 쓰는 최소한의 128비트 정수 연산. netstandard2.1에는 UInt128/Math.BigMul이
    /// 없으므로 직접 구현한다 — 전부 정수 연산이라 플랫폼 무관 결정론.
    /// </summary>
    internal static class Int128Math
    {
        /// <summary>부호 없는 64×64 → 128비트 곱. 32비트 반분할 스쿨북.</summary>
        internal static void Mul64(ulong a, ulong b, out ulong hi, out ulong lo)
        {
            ulong aLo = (uint)a;
            ulong aHi = a >> 32;
            ulong bLo = (uint)b;
            ulong bHi = b >> 32;

            ulong ll = aLo * bLo;
            ulong lh = aLo * bHi;
            ulong hl = aHi * bLo;
            ulong hh = aHi * bHi;

            ulong mid = (ll >> 32) + (uint)lh + (uint)hl;
            lo = (mid << 32) | (uint)ll;
            hi = hh + (lh >> 32) + (hl >> 32) + (mid >> 32);
        }

        /// <summary>
        /// 부호 없는 128비트 ÷ 64비트 → 몫 128비트 + 나머지. 상위 워드 분할 후
        /// Knuth Algorithm D의 2-림 특수화(하드웨어 나눗셈 수 회)로 처리 — 정수 연산만
        /// 사용하므로 결정론이 유지된다. 실측 병목(이진 루프 128회)을 교체한 구현.
        /// </summary>
        internal static void DivRem(
            ulong uHi, ulong uLo, ulong divisor, out ulong qHi, out ulong qLo, out ulong remainder)
        {
            if (divisor == 0)
            {
                throw new DivideByZeroException();
            }

            if (uHi == 0)
            {
                qHi = 0;
                qLo = uLo / divisor;
                remainder = uLo % divisor;
                return;
            }

            if (uHi >= divisor)
            {
                // 상위 워드를 먼저 나눠 몫의 상위 절반을 얻고, 나머지를 하위 나눗셈에 넘긴다:
                // (uHi:uLo)/d = (uHi/d)·2^64 + ((uHi%d):uLo)/d — 정확한 분해.
                qHi = uHi / divisor;
                uHi %= divisor;
            }
            else
            {
                qHi = 0;
            }

            qLo = DivRem128By64(uHi, uLo, divisor, out remainder);
        }

        // (uHi:uLo) ÷ divisor. 전제: uHi < divisor (몫이 64비트에 들어온다).
        // Knuth Algorithm D의 32비트 2-림 특수화(Hacker's Delight udivdi3 계열).
        // 추정 몫의 보정 루프는 최대 2회 — unchecked 랩어라운드는 알고리즘의 일부다.
        private static ulong DivRem128By64(ulong uHi, ulong uLo, ulong divisor, out ulong remainder)
        {
            const ulong Base = 1UL << 32;

            var shift = LeadingZeroCount(divisor);
            var v = divisor << shift;
            var vn1 = v >> 32;
            var vn0 = (uint)v;

            var un32 = shift == 0 ? uHi : (uHi << shift) | (uLo >> (64 - shift));
            var un10 = uLo << shift;
            var un1 = un10 >> 32;
            var un0 = (uint)un10;

            var q1 = un32 / vn1;
            var rhat = un32 % vn1;
            while (q1 >= Base || q1 * vn0 > Base * rhat + un1)
            {
                q1--;
                rhat += vn1;
                if (rhat >= Base)
                {
                    break;
                }
            }

            var un21 = unchecked(un32 * Base + un1 - q1 * v);
            var q0 = un21 / vn1;
            rhat = un21 % vn1;
            while (q0 >= Base || q0 * vn0 > Base * rhat + un0)
            {
                q0--;
                rhat += vn1;
                if (rhat >= Base)
                {
                    break;
                }
            }

            remainder = unchecked(un21 * Base + un0 - q0 * v) >> shift;
            return q1 * Base + q0;
        }

        // netstandard2.1에는 BitOperations.LeadingZeroCount가 없다 — 이진 축소 6단계.
        private static int LeadingZeroCount(ulong value)
        {
            var count = 0;
            if ((value >> 32) == 0) { count += 32; value <<= 32; }
            if ((value >> 48) == 0) { count += 16; value <<= 16; }
            if ((value >> 56) == 0) { count += 8; value <<= 8; }
            if ((value >> 60) == 0) { count += 4; value <<= 4; }
            if ((value >> 62) == 0) { count += 2; value <<= 2; }
            if ((value >> 63) == 0) { count += 1; }
            return count;
        }
    }
}
