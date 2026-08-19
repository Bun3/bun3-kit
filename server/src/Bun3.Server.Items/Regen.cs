using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Time-elapsed lazy settlement formula — the "refill in bulk at access time" pattern for
    /// ticket-like items and stamina. No timer: the game injects the current time when calling.
    /// Applying the amount is the game's job:
    /// <code>
    /// var granted = Regen.SettlePeriodic(count, max, period, now, ref state.RegenRefreshTicks);
    /// if (granted &gt; 0) { inventory.TryAdd(ticketId, granted); instance.MarkChanged(); }
    /// </code>
    /// </summary>
    public static class Regen
    {
        /// <summary>
        /// Settles one unit per period. The basis timestamp advances only by the consumed periods,
        /// preserving the remaining elapsed time (no drift across repeated calls).
        /// For continuous regen of r per second, use period = 1 second / r.
        /// </summary>
        /// <param name="currentCount">Current held count.</param>
        /// <param name="maxCount">Regen cap — if already reached, returns 0 and resets the basis to
        /// now (prevents accruing elapsed time while full).</param>
        /// <param name="periodTicks">Time to refill one unit (ticks, positive).</param>
        /// <param name="nowTicksUtc">Current time (UTC ticks) — game-injected.</param>
        /// <param name="lastRegenTicksUtc">Basis timestamp. 0 (uninitialized) initializes to now and
        /// returns 0 — prevents the classic "full on first settlement" bug. Future values (clock
        /// going backwards) also reset to now and return 0.</param>
        /// <returns>Amount refilled this settlement (0 or more).</returns>
        public static long SettlePeriodic(
            long currentCount,
            long maxCount,
            long periodTicks,
            long nowTicksUtc,
            ref long lastRegenTicksUtc)
        {
            if (periodTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(periodTicks), periodTicks, "Period must be positive.");
            }

            if (lastRegenTicksUtc == 0 || lastRegenTicksUtc > nowTicksUtc)
            {
                lastRegenTicksUtc = nowTicksUtc;
                return 0;
            }

            if (currentCount >= maxCount)
            {
                lastRegenTicksUtc = nowTicksUtc;
                return 0;
            }

            var elapsedPeriods = (nowTicksUtc - lastRegenTicksUtc) / periodTicks;
            var granted = Math.Min(maxCount - currentCount, elapsedPeriods);
            if (granted <= 0)
            {
                return 0;
            }

            if (currentCount + granted >= maxCount)
            {
                lastRegenTicksUtc = nowTicksUtc;   // Cap reached — discard remaining accrual.
            }
            else
            {
                lastRegenTicksUtc += periodTicks * granted;
            }

            return granted;
        }
    }
}
