#nullable enable

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>게임 카탈로그를 개발 캐시 또는 게시 산출물로 만드는 방식을 구분합니다.</summary>
    public enum CatalogBuildMode
    {
        /// <summary>고정 버전 0.0.0-dev를 사용하는 로컬 개발 빌드입니다.</summary>
        Development,

        /// <summary>명시적인 배포 버전을 사용하는 게시 빌드입니다.</summary>
        Published,
    }
}
