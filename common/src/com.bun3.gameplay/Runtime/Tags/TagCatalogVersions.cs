#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>GameplayTag Catalog의 예약된 개발 Version과 게시 가능 여부를 정의합니다.</summary>
    public static class TagCatalogVersions
    {
        /// <summary>로컬 개발 Catalog에만 허용되는 예약 Version입니다.</summary>
        public const string Development = "0.0.0-dev";

        /// <summary>입력 Version이 정확히 예약된 개발 Version인지 확인합니다.</summary>
        /// <param name="catalogVersion">검사할 Catalog Version입니다.</param>
        /// <returns>정확히 개발 Version이면 <see langword="true"/>입니다.</returns>
        public static bool IsDevelopment(string? catalogVersion) =>
            string.Equals(catalogVersion, Development, StringComparison.Ordinal);

        /// <summary>입력 Version이 비어 있지 않고 예약된 개발 Version이 아닌지 확인합니다.</summary>
        /// <param name="catalogVersion">검사할 Catalog Version입니다.</param>
        /// <returns>게시 경계에서 사용할 수 있는 Version이면 <see langword="true"/>입니다.</returns>
        public static bool IsPublished(string? catalogVersion) =>
            !string.IsNullOrWhiteSpace(catalogVersion) && !IsDevelopment(catalogVersion);
    }
}
