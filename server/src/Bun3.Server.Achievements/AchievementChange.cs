namespace Bun3.Server.Achievements
{
    /// <summary>What changed on an achievement.</summary>
    public enum AchievementChangeKind : byte
    {
        /// <summary>The achievement completed one or more times.</summary>
        Completed,

        /// <summary>A pending completion was claimed.</summary>
        Claimed,

        /// <summary>Availability moved between Locked/Ready/Active.</summary>
        AvailabilityChanged,
    }

    /// <summary>One achievement change, carrying the post-change counters so it can serve as an
    /// audit ledger row. Shipped through the action log's structured payload channel; sinks
    /// interpret it by type matching.</summary>
    public readonly struct AchievementChange
    {
        internal AchievementChange(
            AchievementId achievement, AchievementChangeKind kind,
            int completedCount, int claimedCount, AchievementStatus availability)
        {
            Achievement = achievement;
            Kind = kind;
            CompletedCount = completedCount;
            ClaimedCount = claimedCount;
            Availability = availability;
        }

        /// <summary>Target achievement.</summary>
        public AchievementId Achievement { get; }

        /// <summary>What changed.</summary>
        public AchievementChangeKind Kind { get; }

        /// <summary>Completion count immediately after the change.</summary>
        public int CompletedCount { get; }

        /// <summary>Claim count immediately after the change.</summary>
        public int ClaimedCount { get; }

        /// <summary>Availability immediately after the change.</summary>
        public AchievementStatus Availability { get; }
    }
}
