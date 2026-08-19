#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>Immutable index of per-source authoring info for each merged canonical tag.</summary>
    public sealed class TagCatalogProvenance
    {
        private static readonly IReadOnlyList<TagSourceContribution> EmptyContributions =
            Array.AsReadOnly(Array.Empty<TagSourceContribution>());
        private readonly Dictionary<string, IReadOnlyList<TagSourceContribution>> _byCanonicalName;

        internal TagCatalogProvenance(Dictionary<string, IReadOnlyList<TagSourceContribution>> byCanonicalName)
        {
            _byCanonicalName = byCanonicalName ?? throw new ArgumentNullException(nameof(byCanonicalName));
        }

        /// <summary>Gets the source info contributing to a canonical tag, ordered by source ID.</summary>
        /// <param name="canonicalName">Tag path to look up.</param>
        /// <returns>Per-source contributions, or an empty list for unregistered paths.</returns>
        /// <exception cref="ArgumentException"><paramref name="canonicalName"/> has invalid tag syntax.</exception>
        public IReadOnlyList<TagSourceContribution> GetContributions(string canonicalName)
        {
            if (!TagName.TryFold(canonicalName, out var folded))
            {
                throw new ArgumentException("Invalid tag path syntax.", nameof(canonicalName));
            }

            return _byCanonicalName.GetValueOrDefault(folded, EmptyContributions);
        }
    }
}
