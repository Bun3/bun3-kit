#nullable enable
using System;
using System.IO;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Local Development GameplayTag Catalog의 운영체제 공용 cache 경로를 계산합니다.</summary>
    public static class TagCatalogDevelopmentPath
    {
        /// <summary>지정한 Catalog ID의 개발용 <c>GameplayTags.catalog</c> 경로를 반환합니다.</summary>
        /// <param name="catalogId">게임 제품을 나타내는 안정적인 소문자 Catalog ID입니다.</param>
        /// <param name="localApplicationDataOverride">테스트 또는 호스트가 명시하는 LocalApplicationData 대체 경로입니다.</param>
        /// <returns>개발용 Catalog 파일의 절대 경로입니다.</returns>
        /// <exception cref="ArgumentException">Catalog ID 또는 cache root가 올바르지 않은 경우입니다.</exception>
        public static string Get(string catalogId, string? localApplicationDataOverride = null)
        {
            if (!IsValidId(catalogId)) throw new ArgumentException("Catalog ID는 소문자 영숫자 세그먼트를 점 또는 하이픈으로 연결해야 합니다.", nameof(catalogId));
            var root = localApplicationDataOverride
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("LocalApplicationData 경로를 확인할 수 없습니다.", nameof(localApplicationDataOverride));
            return Path.Combine(Path.GetFullPath(root), "Bun3", "GameplayTags", catalogId, "dev", "GameplayTags.catalog");
        }

        private static bool IsValidId(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var separator = true;
            foreach (var character in value!)
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9')) separator = false;
                else if ((character == '.' || character == '-') && !separator) separator = true;
                else return false;
            }

            return !separator;
        }
    }
}
