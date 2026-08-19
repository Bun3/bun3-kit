// ItemInventory partial — regen settlement (SettleRegen) and basis management.
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    // Automatic regen settlement — processes every definition registered with regenPeriodTicks.
    // The basis timestamp lives in a per-definition map, not on instances — regen continues even
    // after the instance disappears at amount 0 (refills from zero).
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>
        /// Lazily settles all regen definitions to the current time — refills are granted
        /// atomically as one batch (onChanged and onApplied once each) and the number of refilled
        /// definitions is returned. Games call this at access points (login, tick, right before
        /// consuming) — no timer needed.
        /// Settling before consuming is the contract — resetting the basis while full prevents
        /// accrual exploits.
        /// </summary>
        /// <param name="nowTicksUtc">Current time (UTC ticks) — game-injected.</param>
        public int SettleRegen(long nowTicksUtc)
        {
            var regenItems = _catalog.RegenItems;
            if (regenItems.Length == 0)
            {
                return 0;
            }

            _applyOps.Clear();
            _regenScratchItems.Clear();
            _regenScratchBasis.Clear();
            for (var i = 0; i < regenItems.Length; i++)
            {
                var item = regenItems[i];
                _regenBasis.TryGetValue(item, out var basis);
                var maxRegen = _catalog.GetMaxRegen(item);
                var quantity = GetQuantity(item);
                if (quantity.CompareTo(maxRegen) >= 0)
                {
                    // At or above the target (including holdings past it up to maxCount) —
                    // regen stops, accrual cleared. Compared without a long conversion
                    // (amounts past the target can exceed the long range).
                    _regenBasis[item] = nowTicksUtc;
                    continue;
                }

                var current = ToInt64Exact(quantity);
                var granted = Regen.SettlePeriodic(
                    current, maxRegen, _catalog.GetRegenPeriodTicks(item), nowTicksUtc, ref basis);
                if (granted > 0)
                {
                    // The basis only advances after a successful grant — on failure the next
                    // settlement retries.
                    _applyOps.Add(new TxOp(TxOpKind.Add, item, 0, granted));
                    _regenScratchItems.Add(item);
                    _regenScratchBasis.Add(basis);
                }
                else
                {
                    _regenBasis[item] = basis;   // Also records initialization and full-state resets.
                }
            }

            if (_applyOps.Count == 0)
            {
                return 0;
            }

            if (CommitOps(_applyOps, out _, null) != InventoryError.None)
            {
                return 0;   // Unreachable guard — basis not advanced, so nothing is lost.
            }

            for (var i = 0; i < _regenScratchItems.Count; i++)
            {
                _regenBasis[_regenScratchItems[i]] = _regenScratchBasis[i];
            }

            return _regenScratchItems.Count;
        }

        /// <summary>Regen basis timestamp for a definition (time of the last completed refill).
        /// 0 if not recorded. Persist by iterating <see cref="ItemCatalog.RegenItems"/> and saving
        /// each definition's basis.</summary>
        public long GetRegenBasis(ItemId item)
        {
            _regenBasis.TryGetValue(item, out var basis);
            return basis;
        }

        /// <summary>For save loading — restores the regen basis (no tracking, no notification).</summary>
        public void LoadRegenBasis(ItemId item, long lastRegenTicksUtc)
        {
            if (!_catalog.Contains(item))
            {
                throw new System.ArgumentOutOfRangeException(nameof(item), "Not an identifier of this catalog.");
            }

            _regenBasis[item] = lastRegenTicksUtc;
        }

        /// <summary>Converts an integer amount to long — regen definitions force integer amounts
        /// at or below maxCount (long), so exact conversion is guaranteed.</summary>
        private static long ToInt64Exact(Bun3.Gameplay.Numerics.BigNum value)
        {
            var result = value.Mantissa;
            for (var i = 0; i < value.Exponent; i++)
            {
                result *= 10;
            }

            return result;
        }
    }
}
