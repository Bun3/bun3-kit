#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Immutable preview compiled from the complete editor workspace sources.</summary>
    public sealed class GameplayTagWorkspaceSnapshot
    {
        private readonly IReadOnlyList<TagSourceDocument> _sources;

        internal GameplayTagWorkspaceSnapshot(
            TagCatalog catalog,
            TagCatalogProvenance provenance,
            IReadOnlyList<TagSourceDocument> sources)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            if (sources is null) throw new ArgumentNullException(nameof(sources));
            var copy = new TagSourceDocument[sources.Count];
            for (var index = 0; index < copy.Length; index++)
            {
                copy[index] = sources[index] ?? throw new ArgumentNullException(nameof(sources));
            }

            _sources = Array.AsReadOnly(copy);
        }

        /// <summary>Immutable runtime catalog merging all sources.</summary>
        public TagCatalog Catalog { get; }

        /// <summary>Index providing per-source authoring info for merged tags.</summary>
        public TagCatalogProvenance Provenance { get; }

        /// <summary>Game and read-only source documents the preview was built from.</summary>
        public IReadOnlyList<TagSourceDocument> Sources => _sources;
    }
}
