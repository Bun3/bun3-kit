using System;

namespace Bun3.Server.Items
{
    /// <summary>카탈로그 구성·검증 실패 — 기동 시점에 발생해 서버 시작을 막아야 하는 오류.</summary>
    public sealed class ItemCatalogException : Exception
    {
        /// <summary>메시지로 예외를 만든다.</summary>
        public ItemCatalogException(string message) : base(message)
        {
        }

        /// <summary>메시지와 내부 예외로 예외를 만든다.</summary>
        public ItemCatalogException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
