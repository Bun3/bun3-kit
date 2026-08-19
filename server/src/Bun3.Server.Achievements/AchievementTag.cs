using System;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// Routing tag identifier — interned by the catalog at startup. Games cache these once at
    /// startup and use only identifiers on hot paths; the string tag never appears at runtime.
    /// Stored as index + 1 so <c>default</c> is <see cref="None"/>. Distinct from
    /// <see cref="AchievementId"/> so the two can never be passed interchangeably.
    /// </summary>
    public readonly struct AchievementTag : IEquatable<AchievementTag>
    {
        private readonly int _indexPlusOne;

        internal AchievementTag(int index)
        {
            _indexPlusOne = index + 1;
        }

        /// <summary>The absent identifier.</summary>
        public static readonly AchievementTag None = default;

        /// <summary>Whether this is <see cref="None"/>.</summary>
        public bool IsNone => _indexPlusOne == 0;

        /// <summary>Catalog-internal index. -1 for None.</summary>
        internal int Index => _indexPlusOne - 1;

        /// <inheritdoc />
        public bool Equals(AchievementTag other) => _indexPlusOne == other._indexPlusOne;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is AchievementTag other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _indexPlusOne;

        /// <summary>Equality operator.</summary>
        public static bool operator ==(AchievementTag a, AchievementTag b) => a.Equals(b);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(AchievementTag a, AchievementTag b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() => IsNone ? "AchievementTag.None" : $"AchievementTag({Index})";
    }
}
