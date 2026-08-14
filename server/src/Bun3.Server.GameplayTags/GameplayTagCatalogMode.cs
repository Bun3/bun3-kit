namespace Bun3.Server.GameplayTags;

/// <summary>서버가 GameplayTag Catalog artifact를 찾고 검증하는 실행 모드입니다.</summary>
public enum GameplayTagCatalogMode
{
    /// <summary>운영체제 공용 개발 cache 또는 사용자별 환경 변수 경로를 사용합니다.</summary>
    LocalDevelopment,

    /// <summary>배포물에 포함된 경로와 고정된 ID, Version, fingerprint를 사용합니다.</summary>
    Packaged,
}
