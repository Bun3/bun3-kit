#nullable enable
using System;
using System.Runtime.InteropServices;

namespace Bun3.Gameplay.Tags
{
    /// <summary>A 2-byte tag index inside a frozen tag catalog.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        private readonly ushort _index;

        internal GameplayTag(ushort index) => _index = index;

        /// <summary>Default value representing the absence of a tag.</summary>
        public static readonly GameplayTag None = default;

        /// <summary>Gets the runtime index used by the current catalog.</summary>
        public ushort Index => _index;

        /// <summary>Indicates whether this is not <see cref="None"/>.</summary>
        public bool IsValid => _index != 0;

        /// <summary>Compares runtime indices with another tag.</summary>
        /// <param name="other">Tag to compare with.</param>
        /// <returns>True if the runtime indices are equal.</returns>
        public bool Equals(GameplayTag other) => _index == other._index;

        /// <summary>Checks whether the given object is a tag with the same runtime index.</summary>
        /// <param name="obj">Object to compare with.</param>
        /// <returns>True if it is the same tag.</returns>
        public override bool Equals(object? obj) => obj is GameplayTag other && Equals(other);

        /// <summary>Builds a hash code from the runtime index.</summary>
        /// <returns>The tag's runtime index.</returns>
        public override int GetHashCode() => _index;

        /// <summary>Checks whether two tags have the same runtime index.</summary>
        public static bool operator ==(GameplayTag left, GameplayTag right) => left.Equals(right);

        /// <summary>Checks whether two tags have different runtime indices.</summary>
        public static bool operator !=(GameplayTag left, GameplayTag right) => !left.Equals(right);
    }
}
