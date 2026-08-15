#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>명시적으로 저장된 게임플레이 태그와 양수 count의 값 쌍입니다.</summary>
    public readonly struct TagCountEntry : IEquatable<TagCountEntry>
    {
        internal TagCountEntry(GameplayTag tag, int count)
        {
            Tag = tag;
            Count = count;
        }

        /// <summary>명시적으로 저장된 태그를 가져옵니다.</summary>
        public GameplayTag Tag { get; }

        /// <summary>태그에 직접 저장된 count를 가져옵니다.</summary>
        public int Count { get; }

        /// <summary>태그와 count가 모두 같은지 비교합니다.</summary>
        public bool Equals(TagCountEntry other) => Tag == other.Tag && Count == other.Count;

        /// <summary>지정한 객체가 같은 태그와 count를 가지는지 비교합니다.</summary>
        public override bool Equals(object? obj) => obj is TagCountEntry other && Equals(other);

        /// <summary>태그와 count를 결합한 hash code를 반환합니다.</summary>
        public override int GetHashCode() => unchecked((Tag.GetHashCode() * 397) ^ Count);

        /// <summary>두 entry의 태그와 count가 모두 같은지 비교합니다.</summary>
        public static bool operator ==(TagCountEntry left, TagCountEntry right) => left.Equals(right);

        /// <summary>두 entry의 태그 또는 count가 다른지 비교합니다.</summary>
        public static bool operator !=(TagCountEntry left, TagCountEntry right) => !left.Equals(right);
    }
}
