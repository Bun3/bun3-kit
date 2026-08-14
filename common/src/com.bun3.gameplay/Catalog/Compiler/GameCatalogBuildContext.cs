#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>host가 카탈로그 컴파일과 게시에 제공하는 하나의 검증된 입력입니다.</summary>
    public sealed class GameCatalogBuildContext
    {
        private readonly IReadOnlyList<TagSourceDocument> _sources;

        /// <summary>빌드할 게임 카탈로그의 식별 정보입니다.</summary>
        public TagCatalogIdentity Identity { get; }

        /// <summary>개발 또는 게시 빌드 방식입니다.</summary>
        public CatalogBuildMode Mode { get; }

        /// <summary>제품 전체에서 resolve한 태그 Source 목록입니다.</summary>
        public IReadOnlyList<TagSourceDocument> Sources => _sources;

        /// <summary>카탈로그 식별 정보, 빌드 방식과 Source를 검증하고 방어적으로 복사합니다.</summary>
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

            var isDevelopmentVersion = string.Equals(
                identity.CatalogVersion,
                "0.0.0-dev",
                StringComparison.Ordinal);
            if ((mode == CatalogBuildMode.Development && !isDevelopmentVersion)
                || (mode == CatalogBuildMode.Published && isDevelopmentVersion))
            {
                throw new ArgumentException("빌드 방식과 Catalog Version이 일치하지 않습니다.", nameof(identity));
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
