#nullable enable
using System;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>Origin that granted an ability. Revocation removes only the grants of one origin.</summary>
    public enum GrantSourceKind : byte
    {
        /// <summary>Granted by the unit archetype at creation.</summary>
        Archetype = 0,
        /// <summary>Granted by an active effect instance; reclaimed when that instance ends.</summary>
        Effect = 1,
        /// <summary>Granted by an item; reclaimed when the item is unequipped or lost.</summary>
        Item = 2,
        /// <summary>Granted directly by game code.</summary>
        Code = 3,
    }

    /// <summary>One grant origin — a kind plus the id identifying the individual origin within that kind.</summary>
    public readonly struct GrantSource : IEquatable<GrantSource>, IComparable<GrantSource>
    {
        private GrantSource(GrantSourceKind kind, ulong key)
        {
            Kind = kind;
            Key = key;
        }

        /// <summary>Kind of the origin.</summary>
        public GrantSourceKind Kind { get; }

        /// <summary>Id identifying the individual origin within its kind; 0 when the kind has a single origin.</summary>
        public ulong Key { get; }

        /// <summary>Creates an archetype origin.</summary>
        public static GrantSource Archetype() => new GrantSource(GrantSourceKind.Archetype, 0);

        /// <summary>Creates an effect-instance origin.</summary>
        /// <param name="instanceId">Effect instance id that grants the ability.</param>
        public static GrantSource Effect(ulong instanceId) => new GrantSource(GrantSourceKind.Effect, instanceId);

        /// <summary>Creates an item origin.</summary>
        /// <param name="itemId">Item id that grants the ability.</param>
        public static GrantSource Item(ulong itemId) => new GrantSource(GrantSourceKind.Item, itemId);

        /// <summary>Creates a game-code origin.</summary>
        /// <param name="key">Optional id distinguishing several code origins.</param>
        public static GrantSource Code(ulong key = 0) => new GrantSource(GrantSourceKind.Code, key);

        /// <summary>Compares with another origin for equality.</summary>
        /// <param name="other">Origin to compare.</param>
        /// <returns>True when kind and key are equal.</returns>
        public bool Equals(GrantSource other) => Kind == other.Kind && Key == other.Key;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is GrantSource other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => unchecked(((int)Kind * 397) ^ Key.GetHashCode());

        /// <summary>Orders by kind then key — the canonical grant order.</summary>
        /// <param name="other">Origin to compare.</param>
        /// <returns>Negative, zero, or positive ordering result.</returns>
        public int CompareTo(GrantSource other)
        {
            var byKind = ((byte)Kind).CompareTo((byte)other.Kind);
            return byKind != 0 ? byKind : Key.CompareTo(other.Key);
        }

        /// <summary>Returns whether two origins are equal.</summary>
        public static bool operator ==(GrantSource left, GrantSource right) => left.Equals(right);

        /// <summary>Returns whether two origins differ.</summary>
        public static bool operator !=(GrantSource left, GrantSource right) => !left.Equals(right);
    }
}
