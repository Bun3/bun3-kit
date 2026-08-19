namespace Bun3.Server.Achievements
{
    /// <summary>
    /// Per-player state for one achievement. Persistence is the game's job, so this is a
    /// serialization-friendly public-field struct — the game reads it via
    /// <see cref="AchievementManager{TDef}.GetState"/> for saving and restores via
    /// <see cref="AchievementManager{TDef}.Restore"/> on load.
    /// Completed/Claimed are not stored here — they derive from the counters.
    /// </summary>
    public struct AchievementState
    {
        /// <summary>Accumulated progress. For repeatable achievements, claiming subtracts one
        /// target; while a claim is pending, progress sits at or above the target (UI shows
        /// min(Progress, Target)/Target). Non-repeatable achievements clamp to the target.</summary>
        public long Progress;

        /// <summary>Completion count. Monotonically increasing, never decreases (the basis of
        /// duplicate-completion prevention). At most 1 for non-repeatables. Repeatable invariant:
        /// CompletedCount = ClaimedCount + Progress/Target.</summary>
        public int CompletedCount;

        /// <summary>Reward claim count. Always at most <see cref="CompletedCount"/>.</summary>
        public int ClaimedCount;

        /// <summary>Last completion time (UTC ticks). 0 = never completed.</summary>
        public long LastCompletedAtUtcTicks;

        /// <summary>Availability — only Locked/Ready/Active are valid (validated by Restore).
        /// Progress accrues only while Active; transitions happen only through the
        /// <see cref="AchievementManager{TDef}"/> Unlock/Activate/Lock methods.</summary>
        public AchievementStatus Availability;
    }
}
