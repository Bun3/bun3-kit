#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Explicit development or published identity to require from the runtime B3DK catalog.</summary>
    public sealed class TagCatalogExpectations
    {
        private readonly byte[]? _expectedFingerprint;

        private TagCatalogExpectations(string catalogId, string catalogVersion, byte[]? expectedFingerprint)
        {
            CatalogId = catalogId;
            CatalogVersion = catalogVersion;
            _expectedFingerprint = expectedFingerprint;
        }

        internal string CatalogId { get; }
        internal string CatalogVersion { get; }
        internal ReadOnlySpan<byte> ExpectedFingerprint => _expectedFingerprint;
        internal bool RequiresFingerprint => _expectedFingerprint is not null;

        /// <summary>Creates expectations requiring the exact catalog ID and the development version.</summary>
        /// <param name="catalogId">Exact catalog ID of the game product.</param>
        /// <returns>Development expectations that do not pin a fingerprint up front.</returns>
        /// <exception cref="ArgumentException"><paramref name="catalogId"/> is empty.</exception>
        public static TagCatalogExpectations ForDevelopment(string catalogId)
        {
            ValidateText(catalogId, nameof(catalogId), "Catalog ID");
            return new TagCatalogExpectations(catalogId, TagCatalogVersions.Development, null);
        }

        /// <summary>Creates expectations requiring the exact catalog ID, version, and an externally pinned fingerprint.</summary>
        /// <param name="catalogId">Exact catalog ID of the game product.</param>
        /// <param name="catalogVersion">Exact version of the published catalog.</param>
        /// <param name="expectedFingerprint">32-byte semantic fingerprint pinned by build metadata.</param>
        /// <returns>Published expectations with defensively copied input.</returns>
        /// <exception cref="ArgumentException">A string is empty or the fingerprint is not 32 bytes.</exception>
        public static TagCatalogExpectations ForPublished(
            string catalogId,
            string catalogVersion,
            ReadOnlySpan<byte> expectedFingerprint)
        {
            ValidateText(catalogId, nameof(catalogId), "Catalog ID");
            ValidateText(catalogVersion, nameof(catalogVersion), "Catalog version");
            if (!TagCatalogVersions.IsPublished(catalogVersion))
            {
                throw new ArgumentException(
                    "The reserved development catalog version cannot be used for published expectations.",
                    nameof(catalogVersion));
            }

            return new TagCatalogExpectations(
                catalogId,
                catalogVersion,
                CopyFingerprint(expectedFingerprint));
        }

        internal static TagCatalogExpectations ForPreparedDevelopment(
            string catalogId,
            ReadOnlySpan<byte> expectedFingerprint)
        {
            ValidateText(catalogId, nameof(catalogId), "Catalog ID");
            return new TagCatalogExpectations(
                catalogId,
                TagCatalogVersions.Development,
                CopyFingerprint(expectedFingerprint));
        }

        private static byte[] CopyFingerprint(ReadOnlySpan<byte> expectedFingerprint)
        {
            if (expectedFingerprint.Length != 32)
            {
                throw new ArgumentException(
                    "Published fingerprint must be exactly 32 bytes.",
                    nameof(expectedFingerprint));
            }

            return expectedFingerprint.ToArray();
        }

        private static void ValidateText(string value, string parameterName, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(label + " cannot be empty.", parameterName);
            }
        }
    }
}
