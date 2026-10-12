#nullable enable
using System;
using System.Buffers.Binary;

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
    /// Random number generator whose position can be saved and restored. A world snapshot requires one, so a
    /// restored world replays the same rolls. The state is opaque bytes of a size the generator chooses.
    /// </summary>
    public interface IRestorableRng : IRng
    {
        /// <summary>Size of the saved state in bytes; constant for a generator.</summary>
        int StateSize { get; }

        /// <summary>Writes the current state.</summary>
        /// <param name="destination">Buffer of at least <see cref="StateSize"/> bytes.</param>
        void SaveState(Span<byte> destination);

        /// <summary>Replaces the current state with one written by <see cref="SaveState"/>. An invalid state must
        /// leave the generator unchanged.</summary>
        /// <param name="source">Exactly <see cref="StateSize"/> bytes.</param>
        void RestoreState(ReadOnlySpan<byte> source);
    }

    /// <summary>
    /// xorshift64* random number generator. A sealed class with mutable state — as a struct,
    /// boxing to IRng or pass/copy by value would silently fork the deterministic stream.
    /// Typically created once at startup, so the class allocation is harmless.
    /// </summary>
    public sealed class XorShiftRng : IRestorableRng
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

        /// <summary>Size of the saved state: the 64-bit state, little-endian.</summary>
        public int StateSize => sizeof(ulong);

        /// <summary>Writes the current state.</summary>
        /// <param name="destination">Buffer of at least <see cref="StateSize"/> bytes.</param>
        /// <exception cref="ArgumentException">Thrown when the buffer is shorter than <see cref="StateSize"/>.</exception>
        public void SaveState(Span<byte> destination)
        {
            if (destination.Length < StateSize)
                throw new ArgumentException("Destination is shorter than StateSize.", nameof(destination));
            BinaryPrimitives.WriteUInt64LittleEndian(destination, _state);
        }

        /// <summary>Replaces the current state with one written by <see cref="SaveState"/>.</summary>
        /// <param name="source">Exactly <see cref="StateSize"/> bytes.</param>
        /// <exception cref="ArgumentException">Thrown, leaving the state unchanged, when the length is not
        /// <see cref="StateSize"/> or the state is zero.</exception>
        public void RestoreState(ReadOnlySpan<byte> source)
        {
            if (source.Length != StateSize)
                throw new ArgumentException("State must be exactly StateSize bytes.", nameof(source));
            var state = BinaryPrimitives.ReadUInt64LittleEndian(source);
            if (state == 0) throw new ArgumentException("State cannot be zero.", nameof(source));
            _state = state;
        }
    }
}
