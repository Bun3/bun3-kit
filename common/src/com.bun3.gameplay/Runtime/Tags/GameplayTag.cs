using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// 계층 태그의 무할당 핸들. 정체성은 점 구분 계층 문자열("State.Dead.Ghost")이며,
    /// 등록한 <see cref="TagRegistry"/> 안에서만 유효하다 — 서로 다른 레지스트리의
    /// 핸들을 섞지 말 것(프로세스당 레지스트리 1개가 표준).
    /// </summary>
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        /// <summary>레지스트리 내부 핸들. 0 = None.</summary>
        public readonly int Handle;

        internal GameplayTag(int handle)
        {
            Handle = handle;
        }

        /// <summary>무효 태그(기본값).</summary>
        public static readonly GameplayTag None = default;

        /// <summary>등록된 태그인지 여부.</summary>
        public bool IsValid => Handle != 0;

        /// <summary>핸들 동등성.</summary>
        public bool Equals(GameplayTag other) => Handle == other.Handle;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is GameplayTag other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Handle;

        /// <summary>동등 비교.</summary>
        public static bool operator ==(GameplayTag a, GameplayTag b) => a.Handle == b.Handle;

        /// <summary>비동등 비교.</summary>
        public static bool operator !=(GameplayTag a, GameplayTag b) => a.Handle != b.Handle;
    }
}
