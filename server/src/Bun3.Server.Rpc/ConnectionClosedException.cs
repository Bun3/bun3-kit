using System;

namespace Bun3.Server.Rpc
{
    /// <summary>응답 대기 중 연결이 닫혀 요청이 완료될 수 없을 때 pending await에 전달된다.</summary>
    public sealed class ConnectionClosedException : Exception
    {
        /// <summary>지정한 메시지로 예외를 생성한다.</summary>
        public ConnectionClosedException(string message) : base(message) { }
    }
}
