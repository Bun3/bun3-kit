using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Abstractions
{
    /// <summary>연결을 받아들이는 쪽의 계약. StopAsync는 신규 수락만 중단한다(기존 연결 종료는 상위 책임).</summary>
    public interface ITransportListener
    {
        Task StartAsync(IConnectionHandler handler, CancellationToken ct = default);
        Task StopAsync(CancellationToken ct = default);
    }
}
