#nullable enable

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>Severity of a tag catalog compilation diagnostic.</summary>
    public enum TagCatalogDiagnosticSeverity
    {
        /// <summary>Warning that does not block producing the compilation result.</summary>
        Warning,

        /// <summary>Error that blocks producing the compilation result.</summary>
        Error,
    }
}
