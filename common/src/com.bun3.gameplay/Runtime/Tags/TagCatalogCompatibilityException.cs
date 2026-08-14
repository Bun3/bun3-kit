#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>B3DK 카탈로그가 실행 파일이 요구한 ID, Version 또는 fingerprint와 다를 때 발생합니다.</summary>
    public sealed class TagCatalogCompatibilityException : Exception
    {
        /// <summary>호환성 오류 설명으로 예외를 만듭니다.</summary>
        /// <param name="message">호환성 오류 설명입니다.</param>
        public TagCatalogCompatibilityException(string message) : base(message)
        {
        }
    }
}
