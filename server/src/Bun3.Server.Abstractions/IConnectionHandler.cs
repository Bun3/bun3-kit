using System;

namespace Bun3.Server.Abstractions
{
    /// <summary>
    /// 전송 이벤트 수신자(Core가 구현). 전송 구현은 다음 순서 계약을 반드시 지킨다:
    /// (1) OnConnected가 반환되기 전에는 해당 연결의 OnPacket/OnClosed를 호출하지 않는다.
    /// (2) OnClosed는 연결당 정확히 1회 호출한다.
    /// (3) OnPacket의 배열 소유권은 수신자에게 이전된다 — 전송 구현은 호출 후 그 배열을
    ///     재사용·변경하지 않는다(수신자가 복사 없이 큐잉할 수 있게 하는 무할당 계약).
    /// </summary>
    public interface IConnectionHandler
    {
        /// <summary>새 연결이 수립되었을 때 전송 구현이 호출한다.</summary>
        void OnConnected(IConnection connection);
        /// <summary>패킷 한 건이 수신되었을 때 전송 구현이 호출한다. 배열 소유권은 수신자에게 이전된다.</summary>
        void OnPacket(IConnection connection, byte[] packet);
        /// <summary>정상 종료면 error는 null.</summary>
        void OnClosed(IConnection connection, Exception? error);
    }
}
