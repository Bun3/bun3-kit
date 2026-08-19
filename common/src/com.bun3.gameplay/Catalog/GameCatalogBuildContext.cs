#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>Single validated input the host provides for catalog compilation and publishing.</summary>
    public sealed class GameCatalogBuildContext
    {
        private readonly IReadOnlyList<TagSourceDocument> _sources;

        /// <summary>Identity of the game catalog to build.</summary>
        public TagCatalogIdentity Identity { get; }

        /// <summary>Development or publish build mode.</summary>
        public CatalogBuildMode Mode { get; }

        /// <summary>Tag sources resolved across the whole product.</summary>
        public IReadOnlyList<TagSourceDocument> Sources => _sources;

        /// <summary>Validates and defensively copies the catalog identity, build mode, and sources.</summary>
        public GameCatalogBuildContext(
            TagCatalogIdentity identity,
            CatalogBuildMode mode,
            IReadOnlyList<TagSourceDocument> sources)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            if (mode != CatalogBuildMode.Development && mode != CatalogBuildMode.Published)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            var isDevelopmentVersion = TagCatalogVersions.IsDevelopment(identity.CatalogVersion);
            if ((mode == CatalogBuildMode.Development && !isDevelopmentVersion)
                || (mode == CatalogBuildMode.Published && isDevelopmentVersion))
            {
                throw new ArgumentException("Build mode and catalog version do not match.", nameof(identity));
            }

            if (sources is null) throw new ArgumentNullException(nameof(sources));
            var sourceCopy = new TagSourceDocument[sources.Count];
            for (var i = 0; i < sourceCopy.Length; i++)
            {
                sourceCopy[i] = sources[i] ?? throw new ArgumentNullException(nameof(sources));
            }

            _sources = Array.AsReadOnly(sourceCopy);
            Mode = mode;
        }
    }
}
