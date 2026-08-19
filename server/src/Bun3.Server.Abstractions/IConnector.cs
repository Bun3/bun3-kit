using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Abstractions
{
    /// <summary>
    /// Contract for outgoing (client-side) connections. Transport implementations follow the same
    /// ordering contract as listeners: handler.OnConnected is invoked before ConnectAsync returns,
    /// no OnPacket/OnClosed occurs before that, and OnClosed fires exactly once per connection.
    /// </summary>
    public interface IConnector
    {
        /// <summary>Establishes the connection and starts receiving. Throws a transport-specific exception on failure.</summary>
        ValueTask<IConnection> ConnectAsync(IConnectionHandler handler, CancellationToken ct = default);
    }
}
