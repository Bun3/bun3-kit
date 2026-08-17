using System;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 팝업 종류를 구분하는 무할당 키. 게임은 자체 enum을 <see cref="int"/>로 캐스팅해 쓴다.
    /// </summary>
    /// <remarks>
    /// 문자열 키를 쓰지 않는 이유: 핫패스 문자열 할당 금지 규율과 오타 방지.
    /// </remarks>
    public readonly struct PopupKey : IEquatable<PopupKey>
    {
        /// <summary>키 원시 값.</summary>
        public readonly int Value;

        /// <summary>원시 값으로 키를 만든다. 게임 enum은 <c>(int)</c> 캐스팅으로 넘긴다.</summary>
        public PopupKey(int value) => Value = value;

        /// <summary><c>stack.Push(1)</c>처럼 int 리터럴/enum 캐스팅을 바로 받기 위한 암시적 변환.</summary>
        public static implicit operator PopupKey(int value) => new(value);

        /// <summary>원시 값 동등 비교.</summary>
        public bool Equals(PopupKey other) => Value == other.Value;

        /// <summary>박싱된 <see cref="PopupKey"/>와의 동등 비교.</summary>
        public override bool Equals(object obj) => obj is PopupKey other && Equals(other);

        /// <summary>원시 값을 그대로 해시로 쓴다.</summary>
        public override int GetHashCode() => Value;

        /// <summary>원시 값 동등 비교.</summary>
        public static bool operator ==(PopupKey left, PopupKey right) => left.Value == right.Value;

        /// <summary>원시 값 비동등 비교.</summary>
        public static bool operator !=(PopupKey left, PopupKey right) => left.Value != right.Value;

        /// <summary>원시 값의 십진 문자열. 디버그 표시용 — 핫패스에서 부르지 말 것.</summary>
        public override string ToString() => Value.ToString();
    }
}
