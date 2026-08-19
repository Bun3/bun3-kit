using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>Reward table entry — a weight and an amount roll range.</summary>
    public readonly struct RewardEntry
    {
        /// <summary>Creates the entry. Data errors throw immediately (startup validation).</summary>
        /// <param name="item">Item to grant.</param>
        /// <param name="weight">Weighted-draw weight (0 or more — 0 is meaningful only in grantAll groups).</param>
        /// <param name="minAmount">Amount roll lower bound (positive).</param>
        /// <param name="maxAmount">Amount roll upper bound (at least the lower bound). Equal bounds mean a fixed amount.</param>
        public RewardEntry(ItemId item, int weight, long minAmount, long maxAmount)
        {
            if (item.IsNone)
            {
                throw new ArgumentException("Reward entry item is None.", nameof(item));
            }

            if (weight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "Weight must be 0 or greater.");
            }

            if (minAmount <= 0 || maxAmount < minAmount)
            {
                throw new ArgumentOutOfRangeException(nameof(minAmount), $"{minAmount}~{maxAmount}", "Invalid amount range.");
            }

            Item = item;
            Weight = weight;
            MinAmount = minAmount;
            MaxAmount = maxAmount;
        }

        /// <summary>Item to grant.</summary>
        public ItemId Item { get; }

        /// <summary>Weighted-draw weight.</summary>
        public int Weight { get; }

        /// <summary>Amount roll lower bound.</summary>
        public long MinAmount { get; }

        /// <summary>Amount roll upper bound.</summary>
        public long MaxAmount { get; }
    }

    /// <summary>Reward group — a trigger probability (permyriad) and a grant mode (all / weighted single).</summary>
    public sealed class RewardGroup
    {
        private readonly RewardEntry[] _entries;

        /// <summary>Creates the group. Data errors throw immediately (startup validation).</summary>
        /// <param name="probabilityPermyriad">Trigger probability in permyriad (0–10000). 10000 =
        /// guaranteed — 0 and 10000 consume no random number when sampling (deterministic
        /// simulation friendly).</param>
        /// <param name="grantAll">true grants every entry; false draws one weighted entry.</param>
        /// <param name="entries">Entries. Weighted-draw groups require a positive weight sum.</param>
        public RewardGroup(int probabilityPermyriad, bool grantAll, params RewardEntry[] entries)
        {
            if (probabilityPermyriad < 0 || probabilityPermyriad > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(probabilityPermyriad), probabilityPermyriad, "Permyriad must be 0–10000.");
            }

            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            long totalWeight = 0;
            foreach (var entry in entries)
            {
                totalWeight += entry.Weight;
            }

            if (!grantAll && totalWeight <= 0)
            {
                throw new ArgumentException("Weighted-draw groups require a positive weight sum.", nameof(entries));
            }

            ProbabilityPermyriad = probabilityPermyriad;
            GrantAll = grantAll;
            _entries = (RewardEntry[])entries.Clone();
            TotalWeight = totalWeight;
        }

        /// <summary>Trigger probability in permyriad.</summary>
        public int ProbabilityPermyriad { get; }

        /// <summary>Whether every entry is granted (false = one weighted draw).</summary>
        public bool GrantAll { get; }

        internal long TotalWeight { get; }

        /// <summary>Entry list.</summary>
        public ReadOnlySpan<RewardEntry> Entries => _entries;
    }

    /// <summary>
    /// Probabilistic reward table: group trigger probability → grant-all or one weighted draw →
    /// amount roll. Gates (level requirements, exclusion lists), mail fallback, and pity systems
    /// are the game's job — the game builds situational tables or post-processes results.
    /// <see cref="Sample"/> is for previews and mail; for granting,
    /// <see cref="ItemInventory{TState}.TryGrant"/> bundles sample → atomic grant into one call.
    /// </summary>
    public sealed class RewardTable
    {
        private readonly RewardGroup[] _groups;

        /// <summary>Creates the table (array copied — once at startup).</summary>
        public RewardTable(RewardGroup[] groups)
        {
            if (groups == null)
            {
                throw new ArgumentNullException(nameof(groups));
            }

            _groups = (RewardGroup[])groups.Clone();
        }

        /// <summary>Group list.</summary>
        public ReadOnlySpan<RewardGroup> Groups => _groups;

        /// <summary>Samples the table, appending positive deltas to the buffer (not cleared).
        /// Groups that fail to trigger add nothing.</summary>
        public void Sample(IRandomSource rng, List<ItemDelta> buffer)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            foreach (var group in _groups)
            {
                if (group.ProbabilityPermyriad == 0)
                {
                    continue;
                }

                if (group.ProbabilityPermyriad < 10000 && rng.Next(10000) >= group.ProbabilityPermyriad)
                {
                    continue;
                }

                var entries = group.Entries;
                if (group.GrantAll)
                {
                    for (var i = 0; i < entries.Length; i++)
                    {
                        buffer.Add(new ItemDelta(entries[i].Item, RollAmount(entries[i], rng)));
                    }

                    continue;
                }

                var roll = rng.Next(group.TotalWeight);
                for (var i = 0; i < entries.Length; i++)
                {
                    roll -= entries[i].Weight;
                    if (roll < 0)
                    {
                        buffer.Add(new ItemDelta(entries[i].Item, RollAmount(entries[i], rng)));
                        break;
                    }
                }
            }
        }

        private static long RollAmount(in RewardEntry entry, IRandomSource rng) =>
            entry.MinAmount == entry.MaxAmount
                ? entry.MinAmount
                : entry.MinAmount + rng.Next(entry.MaxAmount - entry.MinAmount + 1);
    }
}
