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

        public PopupKey(int value) => Value = value;

        public static implicit operator PopupKey(int value) => new(value);

        public bool Equals(PopupKey other) => Value == other.Value;

        public override bool Equals(object obj) => obj is PopupKey other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(PopupKey left, PopupKey right) => left.Value == right.Value;

        public static bool operator !=(PopupKey left, PopupKey right) => left.Value != right.Value;

        public override string ToString() => Value.ToString();
    }
}
