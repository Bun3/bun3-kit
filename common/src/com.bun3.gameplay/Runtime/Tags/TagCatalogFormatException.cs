#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>B3DK 카탈로그의 형식, 손상 또는 구조가 유효하지 않을 때 발생합니다.</summary>
    public sealed class TagCatalogFormatException : Exception
    {
        /// <summary>오류 설명으로 예외를 만듭니다.</summary>
        /// <param name="message">형식 오류 설명입니다.</param>
        public TagCatalogFormatException(string message) : base(message)
        {
        }

        /// <summary>오류 설명과 원인 예외로 예외를 만듭니다.</summary>
        /// <param name="message">형식 오류 설명입니다.</param>
        /// <param name="innerException">형식 오류를 일으킨 원인 예외입니다.</param>
        public TagCatalogFormatException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
