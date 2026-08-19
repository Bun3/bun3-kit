#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Pair of an explicitly stored gameplay tag and its positive count.</summary>
    public readonly struct TagCountEntry : IEquatable<TagCountEntry>
    {
        internal TagCountEntry(GameplayTag tag, int count)
        {
            Tag = tag;
            Count = count;
        }

        /// <summary>Gets the explicitly stored tag.</summary>
        public GameplayTag Tag { get; }

        /// <summary>Gets the count stored directly on the tag.</summary>
        public int Count { get; }

        /// <summary>Checks whether both the tag and the count are equal.</summary>
        public bool Equals(TagCountEntry other) => Tag == other.Tag && Count == other.Count;

        /// <summary>Checks whether the given object has the same tag and count.</summary>
        public override bool Equals(object? obj) => obj is TagCountEntry other && Equals(other);

        /// <summary>Returns a hash code combining the tag and the count.</summary>
        public override int GetHashCode() => unchecked((Tag.GetHashCode() * 397) ^ Count);

        /// <summary>Checks whether two entries have the same tag and count.</summary>
        public static bool operator ==(TagCountEntry left, TagCountEntry right) => left.Equals(right);

        /// <summary>Checks whether two entries differ in tag or count.</summary>
        public static bool operator !=(TagCountEntry left, TagCountEntry right) => !left.Equals(right);
    }
}
