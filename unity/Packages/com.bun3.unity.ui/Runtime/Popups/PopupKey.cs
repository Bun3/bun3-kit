using System;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 팝업 식별 키. 기본 기조는 <b>팝업 타입 자체가 키</b>다 — <see cref="Of{TPopup}"/>가
    /// 클래스 이름을 키로 쓰고, 같은 클래스로 다른 프리팹을 띄우는 변형만 이름을 따로 지정한다.
    /// 서버/테이블 데이터가 여는 경로는 문자열에서 암시적 변환으로 만든다.
    /// </summary>
    /// <remarks>
    /// 동등성은 <see cref="Name"/>(ordinal)만으로 판정한다 — 타입 경로로 연 팝업과 같은 이름의
    /// 데이터 경로 요청이 올바르게 중복 판정된다. 팩토리는 <see cref="Name"/>을 그대로
    /// 로드 주소(Addressables/Resources 키)로 쓰면 된다. 클래스 이름이 곧 식별자이므로
    /// 네임스페이스만 다른 동명 팝업 클래스는 두지 말 것.
    /// 비교는 무할당이며, 타입 키 이름은 제네릭 정적 캐시로 기동 시 1회만 만들어진다.
    /// </remarks>
    public readonly struct PopupKey : IEquatable<PopupKey>
    {
        /// <summary>유일 식별자 — 보통 팝업 클래스 이름, 변형 프리팹이면 지정한 이름. 동등성 기준.</summary>
        public readonly string Name;

        /// <summary>키를 만든 팝업 타입(타입 경로일 때만). 동등성에 참여하지 않는 메타데이터.</summary>
        public readonly Type PopupType;

        /// <summary>데이터 경로용 — 이름만으로 키를 만든다.</summary>
        public PopupKey(string name) : this(name, null) { }

        /// <summary>이름 + 타입 메타로 키를 만든다. 보통은 <see cref="Of{TPopup}"/>를 쓴다.</summary>
        public PopupKey(string name, Type popupType)
        {
            Name = name;
            PopupType = popupType;
        }

        /// <summary>
        /// 타입을 키로 쓴다(기본 기조). <paramref name="popupName"/>을 주면 같은 클래스의
        /// 변형 프리팹을 별도 팝업으로 식별한다.
        /// </summary>
        public static PopupKey Of<TPopup>(string popupName = null) where TPopup : Popup
            => new(popupName ?? TypeName<TPopup>.Value, typeof(TPopup));

        // 타입 키 이름의 기동 시 1회 인터닝.
        private static class TypeName<TPopup> where TPopup : Popup
        {
            internal static readonly string Value = typeof(TPopup).Name;
        }

        /// <summary>서버/테이블 데이터의 팝업 이름을 그대로 받기 위한 암시적 변환.</summary>
        public static implicit operator PopupKey(string name) => new(name);

        /// <summary>이름(ordinal) 동등 비교. 무할당.</summary>
        public bool Equals(PopupKey other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

        /// <summary>박싱된 <see cref="PopupKey"/>와의 동등 비교.</summary>
        public override bool Equals(object obj) => obj is PopupKey other && Equals(other);

        /// <summary>이름의 ordinal 해시.</summary>
        public override int GetHashCode() => Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);

        /// <summary>이름 동등 비교.</summary>
        public static bool operator ==(PopupKey left, PopupKey right) => left.Equals(right);

        /// <summary>이름 비동등 비교.</summary>
        public static bool operator !=(PopupKey left, PopupKey right) => !left.Equals(right);

        /// <summary>키 이름. 디버그 표시용.</summary>
        public override string ToString() => Name ?? string.Empty;
    }
}
