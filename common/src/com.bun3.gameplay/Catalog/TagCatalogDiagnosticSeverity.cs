#nullable enable

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>태그 카탈로그 컴파일 진단의 심각도를 나타냅니다.</summary>
    public enum TagCatalogDiagnosticSeverity
    {
        /// <summary>컴파일 결과 생성을 막지 않는 경고입니다.</summary>
        Warning,

        /// <summary>컴파일 결과 생성을 막는 오류입니다.</summary>
        Error,
    }
}
