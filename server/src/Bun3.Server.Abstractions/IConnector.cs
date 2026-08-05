using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Abstractions
{
    /// <summary>
    /// 나가는 연결(클라이언트 측)의 계약. 전송 구현은 리스너와 동일한 순서 계약을 지킨다:
    /// handler.OnConnected는 ConnectAsync 반환 전에 호출되고, 그 전에는 OnPacket/OnClosed가
    /// 발생하지 않으며, OnClosed는 연결당 정확히 1회다.
    /// </summary>
    public interface IConnector
    {
        /// <summary>연결을 수립하고 수신을 시작한다. 실패 시 전송별 예외를 던진다.</summary>
        ValueTask<IConnection> ConnectAsync(IConnectionHandler handler, CancellationToken ct = default);
    }
}
