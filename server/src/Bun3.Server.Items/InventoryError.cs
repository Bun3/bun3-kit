namespace Bun3.Server.Items
{
    /// <summary>인벤토리 조작의 실패 사유. <see cref="None"/>이 성공이다.</summary>
    public enum InventoryError
    {
        /// <summary>성공.</summary>
        None = 0,

        /// <summary>카탈로그에 없는 아이템(<see cref="ItemId.None"/> 포함).</summary>
        UnknownItem,

        /// <summary>허용되지 않는 수량 — 0 이하(단건)·0(델타)·비스택형의 비정수 또는
        /// 1회 연산 상한(<see cref="ItemInventory{TState}.MaxInstancesPerOperation"/>) 초과.</summary>
        InvalidAmount,

        /// <summary>가용(잠금 제외) 수량 부족. 같은 인스턴스 중복 지정 소모도 여기로 수렴.</summary>
        Insufficient,

        /// <summary>정의당 보유 상한(maxCount) 초과 또는 수량 산술 오버플로.</summary>
        ExceedsMaxCount,

        /// <summary>인벤토리에 없는 인스턴스 id.</summary>
        UnknownInstance,

        /// <summary>이미 존재하는 인스턴스 id 또는 스택형 정의의 중복 인스턴스(로드).</summary>
        DuplicateInstance,

        /// <summary>잠금 플래그(removeBlockingFlags)에 걸린 인스턴스 직접 소모 시도.</summary>
        Locked,
    }
}
