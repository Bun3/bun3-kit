namespace Bun3.Server.Transport.Tcp
{
    /// <summary>Configuration options for TcpConnector.</summary>
    public sealed class TcpConnectorOptions
    {
        /// <summary>Hostname or IP to connect to.</summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>Port to connect to.</summary>
        public int Port { get; set; }

        /// <summary>Maximum inbound packet size. Exceeding it closes the connection as a protocol violation.</summary>
        public int MaxPacketSize { get; set; } = 1024 * 1024;
    }
}
