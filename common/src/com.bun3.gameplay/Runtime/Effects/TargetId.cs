#nullable enable
using System;

namespace Bun3.Gameplay.Effects
{
    /// <summary>64-bit value identifying an effect target.</summary>
    public readonly struct TargetId : IEquatable<TargetId>, IComparable<TargetId>
    {
        /// <summary>Value of the target id.</summary>
        public ulong Value { get; }

        /// <summary>Creates a target id from the given value.</summary>
        /// <param name="value">Value to store.</param>
        public TargetId(ulong value) => Value = value;

        /// <summary>Compares with another target id for equality.</summary>
        /// <param name="other">Target id to compare.</param>
        /// <returns>True if the values are equal.</returns>
        public bool Equals(TargetId other) => Value == other.Value;

        /// <summary>Compares with the given object for equality.</summary>
        /// <param name="obj">Object to compare.</param>
        /// <returns>True if it is the same target id.</returns>
        public override bool Equals(object? obj) => obj is TargetId other && Equals(other);

        /// <summary>Computes a hash code from the value.</summary>
        /// <returns>Hash code of the value.</returns>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Compares with another target id.</summary>
        /// <param name="other">Target id to compare.</param>
        /// <returns>Negative if this value is smaller, 0 if equal, positive if larger.</returns>
        public int CompareTo(TargetId other) => Value.CompareTo(other.Value);

        /// <summary>Returns whether two target ids are equal.</summary>
        public static bool operator ==(TargetId left, TargetId right) => left.Equals(right);

        /// <summary>Returns whether two target ids differ.</summary>
        public static bool operator !=(TargetId left, TargetId right) => !left.Equals(right);

        /// <summary>Returns whether one target id is less than another.</summary>
        public static bool operator <(TargetId left, TargetId right) => left.CompareTo(right) < 0;

        /// <summary>Returns whether one target id is less than or equal to another.</summary>
        public static bool operator <=(TargetId left, TargetId right) => left.CompareTo(right) <= 0;

        /// <summary>Returns whether one target id is greater than another.</summary>
        public static bool operator >(TargetId left, TargetId right) => left.CompareTo(right) > 0;

        /// <summary>Returns whether one target id is greater than or equal to another.</summary>
        public static bool operator >=(TargetId left, TargetId right) => left.CompareTo(right) >= 0;
    }
}
