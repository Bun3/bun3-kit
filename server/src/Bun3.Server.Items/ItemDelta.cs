namespace Bun3.Server.Items
{
    /// <summary>
    /// 트랜잭션의 부호 있는 변경량 — 양수는 지급, 음수는 소모. 0은 거부된다.
    /// 배치는 호출측이 <c>stackalloc</c> Span으로 만들어 무할당으로 넘길 수 있다.
    /// </summary>
    /// <typeparam name="TQuantity">수량 타입.</typeparam>
    public readonly struct ItemDelta<TQuantity>
    {
        /// <summary>변경량을 만든다.</summary>
        /// <param name="item">대상 아이템.</param>
        /// <param name="amount">부호 있는 변경량.</param>
        public ItemDelta(ItemId item, TQuantity amount)
        {
            Item = item;
            Amount = amount;
        }

        /// <summary>대상 아이템.</summary>
        public ItemId Item { get; }

        /// <summary>부호 있는 변경량.</summary>
        public TQuantity Amount { get; }
    }
}
