#nullable enable
using System;

namespace Bun3.Gameplay.Effects
{
    /// <summary>효과 대상을 식별하는 64비트 값입니다.</summary>
    public readonly struct TargetId : IEquatable<TargetId>, IComparable<TargetId>
    {
        /// <summary>대상 식별자의 값입니다.</summary>
        public ulong Value { get; }

        /// <summary>주어진 값으로 대상 식별자를 만듭니다.</summary>
        /// <param name="value">저장할 값입니다.</param>
        public TargetId(ulong value) => Value = value;

        /// <summary>다른 대상 식별자와 같은지 비교합니다.</summary>
        /// <param name="other">비교할 대상입니다.</param>
        /// <returns>값이 같으면 true입니다.</returns>
        public bool Equals(TargetId other) => Value == other.Value;

        /// <summary>지정한 객체와 같은지 비교합니다.</summary>
        /// <param name="obj">비교할 객체입니다.</param>
        /// <returns>같은 대상이면 true입니다.</returns>
        public override bool Equals(object? obj) => obj is TargetId other && Equals(other);

        /// <summary>값을 기반으로 해시 코드를 만듭니다.</summary>
        /// <returns>값의 해시 코드입니다.</returns>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>다른 대상 식별자와 비교합니다.</summary>
        /// <param name="other">비교할 대상입니다.</param>
        /// <returns>이 값이 작으면 음수, 같으면 0, 크면 양수입니다.</returns>
        public int CompareTo(TargetId other) => Value.CompareTo(other.Value);

        /// <summary>두 대상 식별자의 값이 같은지 비교합니다.</summary>
        public static bool operator ==(TargetId left, TargetId right) => left.Equals(right);

        /// <summary>두 대상 식별자의 값이 다른지 비교합니다.</summary>
        public static bool operator !=(TargetId left, TargetId right) => !left.Equals(right);

        /// <summary>한 대상 식별자가 다른 대상보다 작은지 비교합니다.</summary>
        public static bool operator <(TargetId left, TargetId right) => left.CompareTo(right) < 0;

        /// <summary>한 대상 식별자가 다른 대상보다 작거나 같은지 비교합니다.</summary>
        public static bool operator <=(TargetId left, TargetId right) => left.CompareTo(right) <= 0;

        /// <summary>한 대상 식별자가 다른 대상보다 큰지 비교합니다.</summary>
        public static bool operator >(TargetId left, TargetId right) => left.CompareTo(right) > 0;

        /// <summary>한 대상 식별자가 다른 대상보다 크거나 같은지 비교합니다.</summary>
        public static bool operator >=(TargetId left, TargetId right) => left.CompareTo(right) >= 0;
    }
}
