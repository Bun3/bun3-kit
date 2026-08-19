// ItemInventory partial — reward table granting (TryGrant).
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    // Reward table granting — bundles sampling and atomic granting into one call.
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>
        /// Samples a reward table and grants the result all-or-nothing.
        /// <paramref name="failedIndex"/> is the sampled delta's ordinal (on failure such as
        /// exceeding a cap the inventory is fully unchanged — for partial granting, sample via
        /// <see cref="RewardTable.Sample"/> and use <see cref="TryAddUpTo"/> per entry, handling
        /// the remainder with a game fallback such as mail).
        /// </summary>
        public InventoryError TryGrant(
            RewardTable table,
            IRandomSource rng,
            out int failedIndex,
            List<ItemInstance<TState>>? created = null)
        {
            failedIndex = -1;
            if (table == null)
            {
                throw new System.ArgumentNullException(nameof(table));
            }

            _rewardScratch.Clear();
            table.Sample(rng, _rewardScratch);

            _applyOps.Clear();
            for (var i = 0; i < _rewardScratch.Count; i++)
            {
                _applyOps.Add(new TxOp(TxOpKind.Add, _rewardScratch[i].Item, 0, _rewardScratch[i].Amount));
            }

            return CommitOps(_applyOps, out failedIndex, created);
        }
    }
}
