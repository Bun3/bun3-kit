using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 방치형 확장 스택 컨테이너 — 수량 <see cref="BigNum"/>.
    /// 수량 의미론(손실 덧셈)은 <see cref="BigNumQuantityOps"/> 문서를 참고.
    /// </summary>
    public sealed class BigNumItemStackContainer : ItemStackContainer<BigNum, BigNumQuantityOps>
    {
        /// <summary>컨테이너를 만든다. 매개변수는 베이스와 동일.</summary>
        public BigNumItemStackContainer(ItemCatalog catalog, int capacity = 0, Action? onChanged = null)
            : base(catalog, capacity, onChanged)
        {
        }
    }
}
