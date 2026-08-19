using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Exp-to-level settlement formula: drains a per-level required-exp table in a
    /// multi-level-up loop. Applying the result (state save, MarkChanged, level-up
    /// events) is the game's responsibility.
    /// </summary>
    public static class Growth
    {
        /// <summary>
        /// Settles accumulated exp into levels. Multiple levels may be gained at once;
        /// leftover exp is preserved in <paramref name="exp"/>. Stops at max level,
        /// leaving any remaining exp (discarding it is caller policy).
        /// </summary>
        /// <param name="level">Current level (ref — updated after settlement).</param>
        /// <param name="exp">Accumulated exp (ref — remainder preserved after draining).</param>
        /// <param name="maxLevel">Max level — no further gain once reached.</param>
        /// <param name="requiredExpForNext">Exp required to advance from the given level
        /// (must be positive — non-positive is a data error and throws). The game's
        /// exp-table lookup seam.</param>
        /// <returns>Number of levels gained (0 or more).</returns>
        public static int SettleExp(ref int level, ref long exp, int maxLevel, Func<int, long> requiredExpForNext)
        {
            if (requiredExpForNext == null)
            {
                throw new ArgumentNullException(nameof(requiredExpForNext));
            }

            var gained = 0;
            while (level < maxLevel)
            {
                var required = requiredExpForNext(level);
                if (required <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(requiredExpForNext), required, $"Required exp for level {level} must be positive.");
                }

                if (exp < required)
                {
                    break;
                }

                exp -= required;
                level++;
                gained++;
            }

            return gained;
        }
    }
}
