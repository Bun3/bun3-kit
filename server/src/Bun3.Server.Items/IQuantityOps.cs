namespace Bun3.Server.Items
{
    /// <summary>
    /// 수량 산술 시섬. netstandard2.1에는 generic math가 없어 값타입 구현을
    /// 제네릭 제약(<c>where TOps : struct</c>)으로 받아 무박싱 constrained call로 쓴다.
    /// 구현은 무상태여야 한다(컨테이너가 <c>default</c> 인스턴스로 호출).
    /// </summary>
    /// <typeparam name="TQuantity">수량 타입.</typeparam>
    public interface IQuantityOps<TQuantity>
    {
        /// <summary>수량 0.</summary>
        TQuantity Zero { get; }

        /// <summary>대소 비교(음수/0/양수).</summary>
        int Compare(TQuantity a, TQuantity b);

        /// <summary>부호 반전. 컨테이너는 양수 검증을 통과한 값에만 호출한다.</summary>
        TQuantity Negate(TQuantity value);

        /// <summary>카탈로그 maxStack(long) 변환. 양수 입력만 온다.</summary>
        TQuantity FromLong(long value);

        /// <summary>덧셈. 표현 범위를 넘으면 false(전부-아니면-전무 판정에 쓰인다).</summary>
        bool TryAdd(TQuantity a, TQuantity b, out TQuantity result);
    }
}
