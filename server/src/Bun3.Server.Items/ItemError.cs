namespace Bun3.Server.Items
{
    /// <summary>인벤토리 조작의 실패 사유. <see cref="None"/>이 성공이다.</summary>
    public enum ItemError
    {
        /// <summary>성공.</summary>
        None = 0,

        /// <summary>카탈로그에 없는 아이템(<see cref="ItemId.None"/> 포함).</summary>
        UnknownItem,

        /// <summary>허용되지 않는 수량 — 단건 연산의 0 이하, 트랜잭션 델타의 0.</summary>
        InvalidAmount,

        /// <summary>보유 수량 부족.</summary>
        Insufficient,

        /// <summary>스택 상한(maxStack) 초과 또는 수량 산술 오버플로.</summary>
        ExceedsMaxStack,
    }
}
