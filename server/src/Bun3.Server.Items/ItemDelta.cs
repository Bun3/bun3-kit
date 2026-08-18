using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 트랜잭션의 부호 있는 변경량 — 양수는 지급, 음수는 소모. 0은 거부된다.
    /// 배치는 호출측이 <c>stackalloc</c> Span으로 만들어 무할당으로 넘길 수 있다.
    /// 인스턴스 지정 소모가 필요하면 <see cref="ItemInventory{TState}.BeginTransaction"/>을 쓴다.
    /// </summary>
    public readonly struct ItemDelta
    {
        /// <summary>변경량을 만든다.</summary>
        /// <param name="item">대상 아이템.</param>
        /// <param name="amount">부호 있는 변경량(long은 암시 변환).</param>
        public ItemDelta(ItemId item, BigNum amount)
        {
            Item = item;
            Amount = amount;
        }

        /// <summary>대상 아이템.</summary>
        public ItemId Item { get; }

        /// <summary>부호 있는 변경량.</summary>
        public BigNum Amount { get; }
    }
}
