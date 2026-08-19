using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Abstractions
{
    /// <summary>
    /// One connected remote peer. Packet-level send contract independent of the transport
    /// (TCP/Steam/in-process).
    /// </summary>
    public interface IConnection
    {
        /// <summary>
        /// Process-unique connection identifier (monotonically increasing). Used for log
        /// correlation and registry keys. Not an account/player ID; a reconnect gets a new value.
        /// </summary>
        long Id { get; }

        /// <summary>Transport-specific remote address; "IP:port" for TCP, SteamID string for Steam.</summary>
        string? RemoteAddress { get; }

        /// <summary>Whether the connection is still open.</summary>
        bool IsOpen { get; }

        /// <summary>
        /// Sends one packet. Calling on a closed connection is a no-op (never throws).
        /// </summary>
        ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default);

        /// <summary>Closes the connection. Idempotent. The transport then reports OnClosed exactly once.</summary>
        void Close();
    }
}
