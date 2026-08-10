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
        /// 부호 없는 128비트 ÷ 64비트 → 몫 128비트 + 나머지. 이진 롱 디비전(128회 루프) —
        /// 단순하고 자명하게 정확하다. BigNum 연산 빈도(수정자 재계산 수준)에는 충분히 빠르며,
        /// 병목으로 측정되면 Knuth D로 교체한다.
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

            qHi = 0;
            qLo = 0;
            ulong rem = 0;
            for (var i = 127; i >= 0; i--)
            {
                var carry = rem >> 63;
                rem = (rem << 1) | ((i >= 64 ? uHi >> (i - 64) : uLo >> i) & 1);
                if (carry != 0 || rem >= divisor)
                {
                    rem -= divisor;   // carry 시 2^64 초과분이 언더플로 래핑으로 정확히 상쇄된다
                    if (i >= 64)
                    {
                        qHi |= 1UL << (i - 64);
                    }
                    else
                    {
                        qLo |= 1UL << i;
                    }
                }
            }

            remainder = rem;
        }
    }
}
