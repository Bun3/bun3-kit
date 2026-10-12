#nullable enable
using System;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>Stable diagnostic found during tag catalog compilation.</summary>
    public sealed class TagCatalogDiagnostic
    {
        /// <summary>Stable machine-readable diagnostic code.</summary>
        public string Code { get; }

        /// <summary>Severity of the diagnostic.</summary>
        public TagCatalogDiagnosticSeverity Severity { get; }

        /// <summary>Source identifier for the diagnostic, or empty for catalog-wide diagnostics.</summary>
        public string SourceId { get; }

        /// <summary>Origin path or declaration label the diagnostic refers to.</summary>
        public string Origin { get; }

        /// <summary>Canonical tag path the diagnostic refers to.</summary>
        public string CanonicalPath { get; }

        /// <summary>Human-readable diagnostic description.</summary>
        public string Message { get; }

        internal TagCatalogDiagnostic(
            string code,
            TagCatalogDiagnosticSeverity severity,
            string sourceId,
            string origin,
            string canonicalPath,
            string message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Severity = severity;
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            CanonicalPath = canonicalPath ?? throw new ArgumentNullException(nameof(canonicalPath));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }
    }
}
