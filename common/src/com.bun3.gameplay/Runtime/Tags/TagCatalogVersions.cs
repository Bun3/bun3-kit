#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Defines the reserved development version of a GameplayTag catalog and publishability checks.</summary>
    public static class TagCatalogVersions
    {
        /// <summary>Reserved version allowed only for local development catalogs.</summary>
        public const string Development = "0.0.0-dev";

        /// <summary>Checks whether the input version is exactly the reserved development version.</summary>
        /// <param name="catalogVersion">Catalog version to check.</param>
        /// <returns><see langword="true"/> if it is exactly the development version.</returns>
        public static bool IsDevelopment(string? catalogVersion) =>
            string.Equals(catalogVersion, Development, StringComparison.Ordinal);

        /// <summary>Checks whether the input version is non-empty and not the reserved development version.</summary>
        /// <param name="catalogVersion">Catalog version to check.</param>
        /// <returns><see langword="true"/> if the version is usable at a publish boundary.</returns>
        public static bool IsPublished(string? catalogVersion) =>
            !string.IsNullOrWhiteSpace(catalogVersion) && !IsDevelopment(catalogVersion);
    }
}
