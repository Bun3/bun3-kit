using System;

namespace Bun3.Server.Items
{
    /// <summary>기본 스택 컨테이너 — 수량 long. 방치형 대수량은 <see cref="BigNumItemStackContainer"/>.</summary>
    public sealed class ItemStackContainer : ItemStackContainer<long, LongQuantityOps>
    {
        /// <summary>컨테이너를 만든다. 매개변수는 베이스와 동일.</summary>
        public ItemStackContainer(ItemCatalog catalog, int capacity = 0, Action? onChanged = null)
            : base(catalog, capacity, onChanged)
        {
        }
    }
}
