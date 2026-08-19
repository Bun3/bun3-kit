#nullable enable
using System;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Catalog artifact input a Unity publish build embeds and verifies.</summary>
    public sealed class GameplayTagPublishedCatalogContext
    {
        private readonly byte[] _expectedFingerprint;

        /// <summary>Path of the published <c>GameplayTags.catalog</c> artifact.</summary>
        public string ArtifactPath { get; }

        /// <summary>Catalog ID the artifact must have.</summary>
        public string CatalogId { get; }

        /// <summary>Catalog version the artifact must have.</summary>
        public string CatalogVersion { get; }

        /// <summary>32-byte semantic fingerprint pinned by build metadata.</summary>
        public ReadOnlySpan<byte> ExpectedFingerprint => _expectedFingerprint;

        /// <summary>Creates the publish input from the artifact path, identity, and a copied fingerprint.</summary>
        /// <param name="artifactPath">Publish artifact path.</param>
        /// <param name="catalogId">Catalog ID of the game product.</param>
        /// <param name="catalogVersion">Published catalog version.</param>
        /// <param name="expectedFingerprint">32-byte fingerprint pinned by external build metadata.</param>
        /// <exception cref="ArgumentException">A string is empty or the fingerprint is not 32 bytes.</exception>
        public GameplayTagPublishedCatalogContext(
            string artifactPath,
            string catalogId,
            string catalogVersion,
            ReadOnlySpan<byte> expectedFingerprint)
        {
            ArtifactPath = RequireText(artifactPath, nameof(artifactPath), "Artifact path");
            CatalogId = RequireText(catalogId, nameof(catalogId), "Catalog ID");
            CatalogVersion = RequireText(catalogVersion, nameof(catalogVersion), "Catalog Version");
            if (!TagCatalogVersions.IsPublished(CatalogVersion))
            {
                throw new ArgumentException(
                    "The reserved development catalog version cannot be used in a published build.",
                    nameof(catalogVersion));
            }

            if (expectedFingerprint.Length != 32)
            {
                throw new ArgumentException(
                    "The publish fingerprint must be exactly 32 bytes.",
                    nameof(expectedFingerprint));
            }

            _expectedFingerprint = expectedFingerprint.ToArray();
        }

        private static string RequireText(string value, string parameterName, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(label + " cannot be empty.", parameterName);
            }

            return value;
        }
    }
}
