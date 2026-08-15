#nullable enable
using System.Collections.Generic;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>게임 프로젝트의 태그 Catalog 작성 및 게시 입력을 Unity Editor에 제공합니다.</summary>
    public interface IGameplayTagBuildContextProvider
    {
        /// <summary>게임 제품의 안정적인 Catalog ID입니다.</summary>
        string CatalogId { get; }

        /// <summary>제품 의존성 계층이 resolve한 외부 Source Metadata 절대 경로입니다.</summary>
        IReadOnlyList<string> ExternalSourceMetadataPaths { get; }

        /// <summary>Unity 게시 빌드가 고정할 Catalog artifact와 기대값을 가져옵니다.</summary>
        /// <returns>게시된 Catalog의 고정된 입력입니다.</returns>
        GameplayTagPublishedCatalogContext GetPublishedCatalog();
    }
}
