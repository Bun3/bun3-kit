#nullable enable
using System;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>Authoring info one source contributed to a merged tag.</summary>
    public sealed class TagSourceContribution
    {
        /// <summary>Stable identifier of the source that contributed the tag.</summary>
        public string SourceId { get; }

        /// <summary>Source name shown to users.</summary>
        public string DisplayName { get; }

        /// <summary>Origin path or declaration label of the source.</summary>
        public string Origin { get; }

        /// <summary>Comment this source provided, or empty for implied tags.</summary>
        public string Comment { get; }

        /// <summary>Whether this source declared the tag explicitly.</summary>
        public bool IsExplicit { get; }

        /// <summary>Whether this source is read-only.</summary>
        public bool IsReadOnly { get; }

        internal TagSourceContribution(
            string sourceId,
            string displayName,
            string origin,
            string comment,
            bool isExplicit,
            bool isReadOnly)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            Comment = comment ?? throw new ArgumentNullException(nameof(comment));
            IsExplicit = isExplicit;
            IsReadOnly = isReadOnly;
        }
    }
}
