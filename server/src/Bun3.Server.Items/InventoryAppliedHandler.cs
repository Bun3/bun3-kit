using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>One applied change — includes the post-change balance so it can serve as a ledger row.</summary>
    public readonly struct InventoryChange
    {
        internal InventoryChange(ItemId item, BigNum delta, BigNum balance)
        {
            Item = item;
            Delta = delta;
            Balance = balance;
        }

        /// <summary>Target item.</summary>
        public ItemId Item { get; }

        /// <summary>Signed change amount (grant +, consume −).</summary>
        public BigNum Delta { get; }

        /// <summary>Total held for the definition immediately after applying.</summary>
        public BigNum Balance { get; }
    }

    /// <summary>
    /// Applied notification, invoked once per successful commit — net deltas in operation
    /// order plus post-change balances. Intended for machine consumers such as achievement
    /// and quest counting. Contextual audit logging (which action, why) belongs to the
    /// session-wide <see cref="Bun3.Server.Core.ActionLog"/>.
    /// The span is valid only during the call — copy to retain.
    /// </summary>
    /// <param name="applied">The applied changes.</param>
    public delegate void InventoryAppliedHandler(ReadOnlySpan<InventoryChange> applied);
}
