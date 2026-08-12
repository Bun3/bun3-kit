#nullable enable
using System;
using System.Runtime.InteropServices;

namespace Bun3.Gameplay.Tags
{
    /// <summary>동결된 태그 카탈로그 안의 2바이트 태그 인덱스입니다.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        private readonly ushort _index;

        internal GameplayTag(ushort index) => _index = index;

        /// <summary>태그가 없음을 나타내는 기본값입니다.</summary>
        public static readonly GameplayTag None = default;

        /// <summary>현재 카탈로그에서 사용하는 런타임 인덱스를 가져옵니다.</summary>
        public ushort Index => _index;

        /// <summary><see cref="None"/>이 아닌지 나타냅니다.</summary>
        public bool IsValid => _index != 0;

        /// <inheritdoc />
        public bool Equals(GameplayTag other) => _index == other._index;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is GameplayTag other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _index;

        /// <summary>두 태그의 런타임 인덱스가 같은지 비교합니다.</summary>
        public static bool operator ==(GameplayTag left, GameplayTag right) => left.Equals(right);

        /// <summary>두 태그의 런타임 인덱스가 다른지 비교합니다.</summary>
        public static bool operator !=(GameplayTag left, GameplayTag right) => !left.Equals(right);
    }
}
