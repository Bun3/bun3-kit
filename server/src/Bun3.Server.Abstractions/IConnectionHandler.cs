using System;

namespace Bun3.Server.Abstractions
{
    /// <summary>
    /// 전송 이벤트 수신자(Core가 구현). 전송 구현은 다음 순서 계약을 반드시 지킨다:
    /// (1) OnConnected가 반환되기 전에는 해당 연결의 OnPacket/OnClosed를 호출하지 않는다.
    /// (2) OnClosed는 연결당 정확히 1회 호출한다.
    /// (3) OnPacket의 버퍼는 호출 동안만 유효하다(반환 후 재사용될 수 있음).
    /// </summary>
    public interface IConnectionHandler
    {
        /// <summary>새 연결이 수립되었을 때 전송 구현이 호출한다.</summary>
        void OnConnected(IConnection connection);
        /// <summary>패킷 한 건이 수신되었을 때 전송 구현이 호출한다.</summary>
        void OnPacket(IConnection connection, ReadOnlyMemory<byte> packet);
        /// <summary>정상 종료면 error는 null.</summary>
        void OnClosed(IConnection connection, Exception? error);
    }
}
