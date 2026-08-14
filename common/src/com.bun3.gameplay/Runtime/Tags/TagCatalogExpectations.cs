#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>런타임 B3DK 카탈로그에 요구할 명시적인 개발 또는 게시 식별 정보입니다.</summary>
    public sealed class TagCatalogExpectations
    {
        private const string DevelopmentVersion = "0.0.0-dev";
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

        /// <summary>정확한 Catalog ID와 개발 Version을 요구하는 기대 조건을 만듭니다.</summary>
        /// <param name="catalogId">게임 제품의 정확한 Catalog ID입니다.</param>
        /// <returns>fingerprint를 미리 고정하지 않는 개발 기대 조건입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="catalogId"/>가 비어 있는 경우입니다.</exception>
        public static TagCatalogExpectations ForDevelopment(string catalogId)
        {
            ValidateText(catalogId, nameof(catalogId), "Catalog ID");
            return new TagCatalogExpectations(catalogId, DevelopmentVersion, null);
        }

        /// <summary>정확한 Catalog ID, Version과 외부에서 고정한 fingerprint를 요구하는 기대 조건을 만듭니다.</summary>
        /// <param name="catalogId">게임 제품의 정확한 Catalog ID입니다.</param>
        /// <param name="catalogVersion">게시된 Catalog의 정확한 Version입니다.</param>
        /// <param name="expectedFingerprint">빌드 metadata가 고정한 32바이트 semantic fingerprint입니다.</param>
        /// <returns>입력을 방어적으로 복사한 게시 기대 조건입니다.</returns>
        /// <exception cref="ArgumentException">문자열이 비어 있거나 fingerprint가 32바이트가 아닌 경우입니다.</exception>
        public static TagCatalogExpectations ForPublished(
            string catalogId,
            string catalogVersion,
            ReadOnlySpan<byte> expectedFingerprint)
        {
            ValidateText(catalogId, nameof(catalogId), "Catalog ID");
            ValidateText(catalogVersion, nameof(catalogVersion), "Catalog Version");
            if (expectedFingerprint.Length != 32)
            {
                throw new ArgumentException("게시 fingerprint는 정확히 32바이트여야 합니다.", nameof(expectedFingerprint));
            }

            return new TagCatalogExpectations(catalogId, catalogVersion, expectedFingerprint.ToArray());
        }

        private static void ValidateText(string value, string parameterName, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(label + "는 비어 있을 수 없습니다.", parameterName);
            }
        }
    }
}
