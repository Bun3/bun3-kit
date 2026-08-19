using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// Minimal 128-bit integer operations for BigNum. netstandard2.1 has no UInt128/Math.BigMul,
    /// so they are implemented directly — integer-only, hence platform-independent determinism.
    /// </summary>
    internal static class Int128Math
    {
        /// <summary>Unsigned 64×64 → 128-bit multiply. 32-bit half-word schoolbook.</summary>
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
        /// Unsigned 128-bit ÷ 64-bit → 128-bit quotient + remainder. Splits the high word, then a
        /// two-limb specialization of Knuth Algorithm D (a handful of hardware divisions) —
        /// integer-only, so determinism holds.
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
                // Divide the high word first for the upper quotient half, pass the remainder down:
                // (uHi:uLo)/d = (uHi/d)·2^64 + ((uHi%d):uLo)/d — exact decomposition.
                qHi = uHi / divisor;
                uHi %= divisor;
            }
            else
            {
                qHi = 0;
            }

            qLo = DivRem128By64(uHi, uLo, divisor, out remainder);
        }

        // (uHi:uLo) ÷ divisor. Precondition: uHi < divisor (quotient fits in 64 bits).
        // 32-bit two-limb specialization of Knuth Algorithm D.
        // The quotient-estimate correction loops run at most twice — unchecked wraparound is part of the algorithm.
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

        // netstandard2.1 has no BitOperations.LeadingZeroCount — 6-step binary reduction.
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
