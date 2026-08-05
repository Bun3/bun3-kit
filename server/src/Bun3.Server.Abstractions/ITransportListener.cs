using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Abstractions
{
    /// <summary>연결을 받아들이는 쪽의 계약. StopAsync는 신규 수락만 중단한다(기존 연결 종료는 상위 책임).</summary>
    public interface ITransportListener
    {
        /// <summary>수신 대기를 시작하고 이후 연결 이벤트를 handler로 통지한다.</summary>
        Task StartAsync(IConnectionHandler handler, CancellationToken ct = default);
        /// <summary>신규 연결 수락을 중단한다. 기존 연결 종료는 상위 책임이다.</summary>
        Task StopAsync(CancellationToken ct = default);
    }
}
