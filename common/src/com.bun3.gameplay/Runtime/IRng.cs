#nullable enable
using System;

namespace Bun3.Gameplay
{
    /// <summary>Random number generator interface.</summary>
    public interface IRng
    {
        /// <summary>Returns the next unsigned 32-bit random number.</summary>
        /// <returns>Generated random number.</returns>
        uint NextUInt32();
    }

    /// <summary>
    /// xorshift64* random number generator. A sealed class with mutable state — as a struct,
    /// boxing to IRng or pass/copy by value would silently fork the deterministic stream.
    /// Typically created once at startup, so the class allocation is harmless.
    /// </summary>
    public sealed class XorShiftRng : IRng
    {
        private ulong _state;

        /// <summary>Initializes the generator with the given seed.</summary>
        /// <param name="seed">Initial state. Zero is not allowed.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the seed is zero.</exception>
        public XorShiftRng(ulong seed)
        {
            if (seed == 0)
                throw new ArgumentOutOfRangeException(nameof(seed), "Seed cannot be zero.");
            _state = seed;
        }

        /// <summary>Returns the next unsigned 32-bit random number.</summary>
        /// <returns>Generated random number.</returns>
        public uint NextUInt32()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return (uint)((x * 0x2545F4914F6CDD1DUL) >> 32);
        }

        /// <summary>Creates a new instance with the same internal state. Assignment shares the reference,
        /// so call this explicitly to preserve an independent copy of the stream state (e.g. for snapshots).</summary>
        /// <returns>New instance with identical state.</returns>
        public XorShiftRng Clone() => new XorShiftRng(_state);
    }
}
