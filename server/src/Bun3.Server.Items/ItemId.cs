using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 카탈로그에 인터닝된 아이템 정의 식별자. 핫패스는 문자열 대신 이 값으로만 다룬다.
    /// <c>default</c>는 <see cref="None"/>과 같으며 어떤 카탈로그에도 속하지 않는다.
    /// 같은 프로세스에 카탈로그가 여럿이면 교차 사용은 인덱스 범위로만 걸러지므로
    /// 단일 카탈로그 운용이 계약이다.
    /// </summary>
    public readonly struct ItemId : IEquatable<ItemId>
    {
        // 0 = None이 자연 성립하도록 인덱스+1을 저장한다.
        private readonly int _indexPlusOne;

        internal ItemId(int index)
        {
            _indexPlusOne = index + 1;
        }

        /// <summary>무효 식별자 — <c>default(ItemId)</c>와 동일.</summary>
        public static readonly ItemId None = default;

        /// <summary>무효 식별자인지 여부.</summary>
        public bool IsNone => _indexPlusOne == 0;

        /// <summary>카탈로그 내부 인덱스. None이면 -1.</summary>
        internal int Index => _indexPlusOne - 1;

        /// <summary>값 동등 비교.</summary>
        public bool Equals(ItemId other) => _indexPlusOne == other._indexPlusOne;

        /// <summary>값 동등 비교.</summary>
        public override bool Equals(object? obj) => obj is ItemId other && Equals(other);

        /// <summary>해시 코드.</summary>
        public override int GetHashCode() => _indexPlusOne;

        /// <summary>동등 연산자.</summary>
        public static bool operator ==(ItemId a, ItemId b) => a.Equals(b);

        /// <summary>비동등 연산자.</summary>
        public static bool operator !=(ItemId a, ItemId b) => !a.Equals(b);

        /// <summary>디버그용 표현 — 저빈도 경로 전용(문자열 할당).</summary>
        public override string ToString() => IsNone ? "ItemId.None" : $"ItemId({Index})";
    }
}
