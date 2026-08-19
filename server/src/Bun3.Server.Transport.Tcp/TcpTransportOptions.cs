using System.Net;

namespace Bun3.Server.Transport.Tcp
{
    /// <summary>Configuration options for TcpTransportListener.</summary>
    public sealed class TcpTransportOptions
    {
        /// <summary>Listen port. 0 binds to an arbitrary port (check BoundPort).</summary>
        public int Port { get; set; }

        /// <summary>Bind address. Null means all interfaces (Any). Local-only servers should set
        /// <see cref="IPAddress.Loopback"/>.</summary>
        public IPAddress? BindAddress { get; set; }

        /// <summary>Maximum concurrent connections. Excess connections are closed immediately on
        /// accept. Zero or less = unlimited. Caps resource exhaustion (per-session receive queue
        /// memory) from a flood of unauthenticated connections.</summary>
        public int MaxConnections { get; set; } = 1000;

        /// <summary>Maximum inbound packet size. Exceeding it closes the connection as a protocol violation.</summary>
        public int MaxPacketSize { get; set; } = 1024 * 1024;

        /// <summary>Accept backlog size (backlog of TcpListener.Start).</summary>
        public int Backlog { get; set; } = 512;
    }
}
