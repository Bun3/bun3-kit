namespace Bun3.Server.Items
{
    /// <summary>컨테이너 열거 항목 — 보유 중인 스택 하나(수량은 항상 양수).</summary>
    /// <typeparam name="TQuantity">수량 타입.</typeparam>
    public readonly struct ItemStack<TQuantity>
    {
        internal ItemStack(ItemId item, TQuantity quantity)
        {
            Item = item;
            Quantity = quantity;
        }

        /// <summary>아이템.</summary>
        public ItemId Item { get; }

        /// <summary>보유 수량.</summary>
        public TQuantity Quantity { get; }
    }
}
