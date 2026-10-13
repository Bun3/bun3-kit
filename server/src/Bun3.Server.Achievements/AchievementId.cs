using System;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// Achievement identifier — a dense index interned by the catalog at startup. Games cache these
    /// once at startup and use only identifiers on hot paths; the string id never appears at
    /// runtime. Stored as index + 1 so <c>default</c> is <see cref="None"/>. Distinct from
    /// <see cref="AchievementTag"/> so the two can never be passed interchangeably.
    /// </summary>
    public readonly struct AchievementId : IEquatable<AchievementId>
    {
        private readonly int _indexPlusOne;

        internal AchievementId(int index)
        {
            _indexPlusOne = index + 1;
        }

        /// <summary>The absent identifier.</summary>
        public static readonly AchievementId None = default;

        /// <summary>Whether this is <see cref="None"/>.</summary>
        public bool IsNone => _indexPlusOne == 0;

        /// <summary>Catalog-internal index. -1 for None.</summary>
        internal int Index => _indexPlusOne - 1;

        /// <inheritdoc />
        public bool Equals(AchievementId other) => _indexPlusOne == other._indexPlusOne;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is AchievementId other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _indexPlusOne;

        /// <summary>Equality operator.</summary>
        public static bool operator ==(AchievementId a, AchievementId b) => a.Equals(b);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(AchievementId a, AchievementId b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() => IsNone ? "AchievementId.None" : $"AchievementId({Index})";
    }
}
