namespace Bun3.Server.Achievements
{
    /// <summary>
    /// Achievement lifecycle status. Only the availability set (Locked/Ready/Active) is persisted —
    /// <see cref="AchievementState.Availability"/> — while Completed/Claimed are derived from the
    /// counters by <see cref="AchievementManager{TDef}.GetStatus"/> (never stored).
    /// The numeric values are part of the save format; do not change them.
    /// </summary>
    public enum AchievementStatus
    {
        /// <summary>Closed — no progress possible. The "locked" state of content/feature gates,
        /// or excluded from rotation.</summary>
        Locked = 0,

        /// <summary>Open but not started — visible in lists, but progress does not accrue
        /// (daily-mission selection, etc.).</summary>
        Ready = 1,

        /// <summary>Accepting progress.</summary>
        Active = 2,

        /// <summary>Derived only — there are unclaimed completions (claimable count &gt; 0).</summary>
        Completed = 3,

        /// <summary>Derived only — terminal state of a non-repeatable achievement fully completed
        /// and claimed.</summary>
        Claimed = 4,
    }
}
