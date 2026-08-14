namespace Bun3.Server.GameplayTags;

/// <summary>구성 섹션 <c>Bun3:GameplayTags</c>에서 바인딩되는 서버 Catalog 옵션입니다.</summary>
public sealed class GameplayTagCatalogOptions
{
    /// <summary>구성 바인딩에 사용되는 섹션 이름입니다.</summary>
    public const string SectionName = "Bun3:GameplayTags";

    /// <summary>Catalog artifact를 찾고 검증할 실행 모드입니다.</summary>
    public GameplayTagCatalogMode Mode { get; set; }

    /// <summary>게임 제품을 나타내는 정확한 Catalog ID입니다.</summary>
    public string CatalogId { get; set; } = string.Empty;

    /// <summary>Packaged 모드에서 요구할 정확한 Catalog Version입니다.</summary>
    public string CatalogVersion { get; set; } = string.Empty;

    /// <summary>Packaged 모드에서 빌드 metadata가 고정한 64자리 SHA-256 hex입니다.</summary>
    public string ExpectedFingerprint { get; set; } = string.Empty;

    /// <summary>Packaged 모드에서 application base 기준으로 해석할 Catalog 경로입니다.</summary>
    public string PackagedPath { get; set; } = "Content/GameplayTags.catalog";

    internal string? LocalApplicationDataOverride { get; set; }

    internal static bool IsFingerprintHex(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!((character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }

        return true;
    }
}
