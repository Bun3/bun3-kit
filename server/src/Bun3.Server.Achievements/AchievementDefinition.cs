using System;
using System.Collections.Generic;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// Achievement definition base. The framework knows only these fields; definition content
    /// (name, rewards, condition values, etc.) is added by the game in derived classes. Instances
    /// must be treated as immutable after catalog construction — validation is performed in bulk
    /// by the <see cref="AchievementCatalog{TDef}"/> constructor.
    /// </summary>
    public class AchievementDefinition
    {
        /// <summary>Achievement identifier. Interned into an int index at catalog startup; this
        /// string never appears on runtime hot paths. Must be non-empty and unique within the
        /// catalog (ordinal comparison).</summary>
        public string Id { get; }

        /// <summary>Completion target (&gt; 0). Repeatable achievements accrue a completion each
        /// time the target is crossed.</summary>
        public long Target { get; }

        /// <summary>If true, the achievement completes again each time the target is reached
        /// (progress accumulates; claiming subtracts one target); if false, progress clamps to the
        /// target after the single completion.</summary>
        public bool Repeatable { get; }

        /// <summary>Routing/grouping tags. Interned into tag indices at catalog startup, used by
        /// <see cref="AchievementManager{TDef}.IncreaseByTag(int, long)"/> and group sweeps.
        /// Games wanting enum-based management can fill these with nameof (the framework does not care).</summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Initial availability — only Locked/Ready/Active are valid (catalog-validated).
        /// Default Active. Chain tiers start Locked and are opened by
        /// <see cref="AchievementManager{TDef}.Activate"/> in the previous tier's completion hook.</summary>
        public AchievementStatus InitialAvailability { get; }

        /// <summary>Creates the definition. Argument validation happens in bulk at catalog construction.</summary>
        public AchievementDefinition(
            string id,
            long target,
            bool repeatable = false,
            AchievementStatus initialAvailability = AchievementStatus.Active,
            IReadOnlyList<string>? tags = null)
        {
            Id = id;
            Target = target;
            Repeatable = repeatable;
            InitialAvailability = initialAvailability;
            Tags = tags ?? Array.Empty<string>();
        }
    }
}
