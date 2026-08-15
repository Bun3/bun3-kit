#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>게임 제품 카탈로그의 안정적인 ID와 명시적인 배포 버전을 묶습니다.</summary>
    public sealed class TagCatalogIdentity
    {
        /// <summary>게임 제품을 나타내는 안정적인 Catalog ID입니다.</summary>
        public string CatalogId { get; }

        /// <summary>개발 또는 게시 Catalog Version입니다.</summary>
        public string CatalogVersion { get; }

        /// <summary>비어 있지 않은 Catalog ID와 Version으로 식별 정보를 만듭니다.</summary>
        public TagCatalogIdentity(string catalogId, string catalogVersion)
        {
            if (string.IsNullOrWhiteSpace(catalogId))
            {
                throw new ArgumentException("Catalog ID는 비어 있을 수 없습니다.", nameof(catalogId));
            }

            if (string.IsNullOrWhiteSpace(catalogVersion))
            {
                throw new ArgumentException("Catalog Version은 비어 있을 수 없습니다.", nameof(catalogVersion));
            }

            CatalogId = catalogId;
            CatalogVersion = catalogVersion;
        }
    }
}
