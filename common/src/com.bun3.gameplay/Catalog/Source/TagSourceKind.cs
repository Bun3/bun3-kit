#nullable enable

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// 태그 Source 문서가 제공되는 위치와 형식을 구분합니다.
    /// </summary>
    public enum TagSourceKind
    {
        /// <summary>게임 프로젝트가 소유하고 편집하는 JSON Source입니다.</summary>
        GameJson,

        /// <summary>패키지가 제공하는 읽기 전용 JSON Source입니다.</summary>
        PackageJson,

        /// <summary>네이티브 코드가 제공하는 읽기 전용 Source입니다.</summary>
        Native,
    }
}
