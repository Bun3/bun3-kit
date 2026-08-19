using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>Thrown when a BigNum exponent exceeds the representable limit. That scale is
    /// unreachable through legitimate gameplay, so it throws instead of clamping to surface
    /// runaway balance formulas.</summary>
    public sealed class BigNumOverflowException : OverflowException
    {
        /// <summary>Creates the exception with the offending exponent.</summary>
        public BigNumOverflowException(long exponent)
            : base($"BigNum exponent {exponent} exceeded the limit (±{BigNum.MaxExponent}) — suspect a runaway formula.")
        {
        }
    }
}
