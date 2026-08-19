#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Binds the stable ID of a game product catalog to an explicit deployment version.</summary>
    public sealed class TagCatalogIdentity
    {
        /// <summary>Stable catalog ID representing the game product.</summary>
        public string CatalogId { get; }

        /// <summary>Development or published catalog version.</summary>
        public string CatalogVersion { get; }

        /// <summary>Creates the identity from a non-empty catalog ID and version.</summary>
        public TagCatalogIdentity(string catalogId, string catalogVersion)
        {
            if (string.IsNullOrWhiteSpace(catalogId))
            {
                throw new ArgumentException("Catalog ID cannot be empty.", nameof(catalogId));
            }

            if (string.IsNullOrWhiteSpace(catalogVersion))
            {
                throw new ArgumentException("Catalog version cannot be empty.", nameof(catalogVersion));
            }

            CatalogId = catalogId;
            CatalogVersion = catalogVersion;
        }
    }
}
