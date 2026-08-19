using System;

namespace Bun3.Server.Abstractions
{
    /// <summary>
    /// Receiver of transport events (implemented by Core). Transport implementations must honor
    /// this ordering contract:
    /// (1) No OnPacket/OnClosed for a connection before its OnConnected has returned.
    /// (2) OnClosed is invoked exactly once per connection.
    /// (3) OnPacket transfers ownership of the array to the receiver — the transport must not
    ///     reuse or mutate it after the call (allocation-free contract so the receiver can queue
    ///     without copying).
    /// </summary>
    public interface IConnectionHandler
    {
        /// <summary>Invoked by the transport when a new connection is established.</summary>
        void OnConnected(IConnection connection);
        /// <summary>Invoked by the transport when one packet is received. Array ownership transfers to the receiver.</summary>
        void OnPacket(IConnection connection, byte[] packet);
        /// <summary>error is null on a clean close.</summary>
        void OnClosed(IConnection connection, Exception? error);
    }
}
