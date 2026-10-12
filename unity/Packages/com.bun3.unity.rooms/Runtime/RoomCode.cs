using System;
using System.Security.Cryptography;

namespace Bun3.Unity.Rooms
{
    /// <summary>
    /// Six-character room code. The alphabet leaves out look-alike characters (0/O, 1/I) so a code
    /// survives being read aloud. Each character carries five bits; a code packs a 30-bit value.
    /// </summary>
    public readonly struct RoomCode : IEquatable<RoomCode>
    {
        /// <summary>Number of characters in a code.</summary>
        public const int Length = 6;

        /// <summary>Characters a code may contain, in value order.</summary>
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        /// <summary>Largest value a code can encode.</summary>
        public const uint MaxValue = (1u << (5 * Length)) - 1;

        private RoomCode(uint value) => Value = value;

        /// <summary>The 30-bit value this code encodes.</summary>
        public uint Value { get; }

        /// <summary>Creates a uniformly random code from a cryptographic random source.</summary>
        public static RoomCode Generate()
        {
            var bytes = new byte[4];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return new RoomCode(BitConverter.ToUInt32(bytes, 0) & MaxValue);
        }

        /// <summary>Encodes <paramref name="value"/> as a code.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> exceeds <see cref="MaxValue"/>.</exception>
        public static RoomCode FromValue(uint value)
        {
            if (value > MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
            return new RoomCode(value);
        }

        /// <summary>
        /// Parses user input. Surrounding whitespace is ignored and letters are upper-cased; anything
        /// else must be exactly <see cref="Length"/> alphabet characters.
        /// </summary>
        public static bool TryNormalize(ReadOnlySpan<char> input, out RoomCode code)
        {
            code = default;
            input = input.Trim();
            if (input.Length != Length) return false;
            uint value = 0;
            for (int i = 0; i < Length; i++)
            {
                int digit = Alphabet.IndexOf(char.ToUpperInvariant(input[i]));
                if (digit < 0) return false;
                value = (value << 5) | (uint)digit;
            }
            code = new RoomCode(value);
            return true;
        }

        /// <summary>Writes the code's characters without allocating.</summary>
        public bool TryFormat(Span<char> destination, out int charsWritten)
        {
            charsWritten = 0;
            if (destination.Length < Length) return false;
            for (int i = 0; i < Length; i++)
                destination[i] = Alphabet[(int)((Value >> (5 * (Length - 1 - i))) & 31)];
            charsWritten = Length;
            return true;
        }

        /// <summary>Returns the code's characters. Allocates; prefer <see cref="TryFormat"/> on hot paths.</summary>
        public override string ToString()
        {
            Span<char> buffer = stackalloc char[Length];
            TryFormat(buffer, out _);
            return new string(buffer);
        }

        /// <inheritdoc/>
        public bool Equals(RoomCode other) => Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RoomCode other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (int)Value;

        /// <summary>Compares codes by value.</summary>
        public static bool operator ==(RoomCode left, RoomCode right) => left.Equals(right);

        /// <summary>Compares codes by value.</summary>
        public static bool operator !=(RoomCode left, RoomCode right) => !left.Equals(right);
    }
}
