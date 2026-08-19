using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Abstractions
{
    /// <summary>Contract for the accepting side. StopAsync only stops accepting new connections (closing existing ones is the caller's responsibility).</summary>
    public interface ITransportListener
    {
        /// <summary>Starts listening and reports subsequent connection events to the handler.</summary>
        Task StartAsync(IConnectionHandler handler, CancellationToken ct = default);
        /// <summary>Stops accepting new connections. Closing existing connections is the caller's responsibility.</summary>
        Task StopAsync(CancellationToken ct = default);
    }
}
