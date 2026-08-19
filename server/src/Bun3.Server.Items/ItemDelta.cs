using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Signed change amount for a transaction — positive grants, negative consumes. Zero is rejected.
    /// Callers can build batches as <c>stackalloc</c> spans for allocation-free calls.
    /// For instance-targeted consumption use <see cref="ItemInventory{TState}.BeginTransaction"/>.
    /// </summary>
    public readonly struct ItemDelta
    {
        /// <summary>Creates the delta.</summary>
        /// <param name="item">Target item.</param>
        /// <param name="amount">Signed change amount (long converts implicitly).</param>
        public ItemDelta(ItemId item, BigNum amount)
        {
            Item = item;
            Amount = amount;
        }

        /// <summary>Target item.</summary>
        public ItemId Item { get; }

        /// <summary>Signed change amount.</summary>
        public BigNum Amount { get; }
    }
}
