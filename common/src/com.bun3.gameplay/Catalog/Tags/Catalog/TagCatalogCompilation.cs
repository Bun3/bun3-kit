#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>Bundles the runtime result, provenance, and diagnostics of a tag source merge.</summary>
    public sealed class TagCatalogCompilation
    {
        private readonly IReadOnlyList<TagCatalogDiagnostic> _diagnostics;

        internal TagCatalogCompilation(
            TagCatalog? catalog,
            TagCatalogProvenance? provenance,
            TagCatalogDiagnostic[] diagnostics)
        {
            Catalog = catalog;
            Provenance = provenance;
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
            _diagnostics = Array.AsReadOnly((TagCatalogDiagnostic[])diagnostics.Clone());
            Succeeded = catalog is not null && provenance is not null;
        }

        /// <summary>Whether the runtime catalog and provenance were produced without errors.</summary>
        public bool Succeeded { get; }

        /// <summary>Immutable runtime catalog when successful.</summary>
        public TagCatalog? Catalog { get; }

        /// <summary>Immutable source provenance index when successful.</summary>
        public TagCatalogProvenance? Provenance { get; }

        /// <summary>Compilation diagnostics ordered by source and canonical path.</summary>
        public IReadOnlyList<TagCatalogDiagnostic> Diagnostics => _diagnostics;
    }
}
