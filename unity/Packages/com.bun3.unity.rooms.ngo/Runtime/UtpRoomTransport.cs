using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace Bun3.Unity.Rooms.Ngo
{
    /// <summary>Unity Transport on the loopback interface. Endpoints are <c>127.0.0.1:port</c>.</summary>
    public sealed class UtpRoomTransport : INgoRoomTransport
    {
        private const string Loopback = "127.0.0.1";

        /// <summary>Wraps the manager's <see cref="UnityTransport"/>.</summary>
        public UtpRoomTransport(UnityTransport transport) =>
            Transport = transport != null ? transport : throw new ArgumentNullException(nameof(transport));

        /// <summary>The wrapped transport.</summary>
        public UnityTransport Transport { get; }

        /// <summary>Port the host listens on; 0 picks a free port in <see cref="PrepareHost"/>.</summary>
        public ushort ListenPort { get; set; }

        /// <inheritdoc/>
        public string PrepareHost(NetworkManager manager, IRoom room)
        {
            ushort port = ListenPort != 0 ? ListenPort : FreeUdpPort();
            Transport.SetConnectionData(Loopback, port, Loopback);
            manager.NetworkConfig.NetworkTransport = Transport;
            return string.Concat(Loopback, ":", port.ToString(CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        /// <exception cref="FormatException"><paramref name="endpoint"/> is not <c>host:port</c>.</exception>
        public void PrepareClient(NetworkManager manager, IRoom room, string endpoint)
        {
            int split = endpoint != null ? endpoint.LastIndexOf(':') : -1;
            if (split <= 0 || !ushort.TryParse(endpoint.Substring(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out ushort port))
                throw new FormatException("The host endpoint is not host:port.");
            Transport.SetConnectionData(endpoint.Substring(0, split), port);
            manager.NetworkConfig.NetworkTransport = Transport;
        }

        internal static ushort FreeUdpPort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return (ushort)((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }
    }
}
