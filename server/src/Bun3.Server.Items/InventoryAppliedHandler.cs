using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>적용된 변경 1건 — CS 원장(ledger)의 한 줄이 되도록 변경 후 잔량을 포함한다.</summary>
    public readonly struct InventoryChange
    {
        internal InventoryChange(ItemId item, BigNum delta, BigNum balance)
        {
            Item = item;
            Delta = delta;
            Balance = balance;
        }

        /// <summary>대상 아이템.</summary>
        public ItemId Item { get; }

        /// <summary>부호 있는 변경량(지급 +, 소모 −).</summary>
        public BigNum Delta { get; }

        /// <summary>적용 직후의 정의 총 보유량 — "왜 줄었나" CS 문의를 재생 없이 답하는 필드.</summary>
        public BigNum Balance { get; }
    }

    /// <summary>
    /// 성공한 커밋당 1회 호출되는 적용 통지 — 연산 순서대로의 순 델타와 변경 후 잔량.
    /// 업적·퀘스트 카운팅 같은 기계 소비용이다. 문맥(어떤 행동이 왜)을 가진 CS 감사
    /// 원장은 세션 공용 <see cref="ActionLog"/> 쪽이 원천.
    /// span은 호출 동안만 유효 — 보관은 복사로.
    /// </summary>
    /// <param name="applied">적용된 변경들.</param>
    public delegate void InventoryAppliedHandler(ReadOnlySpan<InventoryChange> applied);
}
