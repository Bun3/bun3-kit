#nullable enable
using System;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Unity 게시 빌드가 포함하고 검증할 Catalog artifact 입력입니다.</summary>
    public sealed class GameplayTagPublishedCatalogContext
    {
        private readonly byte[] _expectedFingerprint;

        /// <summary>게시된 <c>GameplayTags.catalog</c> artifact 경로입니다.</summary>
        public string ArtifactPath { get; }

        /// <summary>artifact가 가져야 하는 Catalog ID입니다.</summary>
        public string CatalogId { get; }

        /// <summary>artifact가 가져야 하는 Catalog Version입니다.</summary>
        public string CatalogVersion { get; }

        /// <summary>빌드 metadata가 고정한 32바이트 semantic fingerprint입니다.</summary>
        public ReadOnlySpan<byte> ExpectedFingerprint => _expectedFingerprint;

        /// <summary>artifact 경로, 식별 정보와 복사된 fingerprint로 게시 입력을 만듭니다.</summary>
        /// <param name="artifactPath">게시 artifact 경로입니다.</param>
        /// <param name="catalogId">게임 제품의 Catalog ID입니다.</param>
        /// <param name="catalogVersion">게시된 Catalog Version입니다.</param>
        /// <param name="expectedFingerprint">외부 build metadata가 고정한 32바이트 fingerprint입니다.</param>
        /// <exception cref="ArgumentException">문자열이 비어 있거나 fingerprint 길이가 32바이트가 아닌 경우입니다.</exception>
        public GameplayTagPublishedCatalogContext(
            string artifactPath,
            string catalogId,
            string catalogVersion,
            ReadOnlySpan<byte> expectedFingerprint)
        {
            ArtifactPath = RequireText(artifactPath, nameof(artifactPath), "Artifact path");
            CatalogId = RequireText(catalogId, nameof(catalogId), "Catalog ID");
            CatalogVersion = RequireText(catalogVersion, nameof(catalogVersion), "Catalog Version");
            if (expectedFingerprint.Length != 32)
            {
                throw new ArgumentException(
                    "게시 fingerprint는 정확히 32바이트여야 합니다.",
                    nameof(expectedFingerprint));
            }

            _expectedFingerprint = expectedFingerprint.ToArray();
        }

        private static string RequireText(string value, string parameterName, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(label + "는 비어 있을 수 없습니다.", parameterName);
            }

            return value;
        }
    }
}
