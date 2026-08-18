using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 성공한 커밋당 1회 호출되는 적용 통지 — 연산 순서대로의 순 델타(지급 +, 소모 −).
    /// 업적·퀘스트·랭킹 카운팅의 원천이다(idlez의 ItemAdded/ItemConsume 이벤트 상당).
    /// span은 호출 동안만 유효하다 — 보관하려면 복사할 것.
    /// </summary>
    /// <param name="applied">적용된 델타들.</param>
    public delegate void InventoryAppliedHandler(ReadOnlySpan<ItemDelta> applied);
}
