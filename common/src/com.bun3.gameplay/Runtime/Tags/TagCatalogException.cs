#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// 태그 카탈로그 JSON의 형식 또는 의미가 올바르지 않을 때 발생합니다.
    /// </summary>
    public sealed class TagCatalogException : FormatException
    {
        /// <summary>오류가 난 JSON 경로입니다.</summary>
        public string JsonPath { get; }

        /// <summary>오류가 난 1부터 시작하는 줄 번호입니다.</summary>
        public int LineNumber { get; }

        /// <summary>오류가 난 1부터 시작하는 줄 안 위치입니다.</summary>
        public int LinePosition { get; }

        /// <summary>JSON 위치 정보를 포함한 예외를 만듭니다.</summary>
        public TagCatalogException(string message, string jsonPath, int lineNumber, int linePosition)
            : base(message)
        {
            JsonPath = jsonPath;
            LineNumber = lineNumber;
            LinePosition = linePosition;
        }
    }
}
