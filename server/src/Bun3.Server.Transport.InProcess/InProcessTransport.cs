using System;
using System.Threading;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Transport.InProcess
{
    /// <summary>
    /// Pair factory for the socketless in-process (loopback) transport. One instance issues both
    /// the listener and the connector; each ConnectAsync of <see cref="Connector"/> attaches a new
    /// connection pair to <see cref="Listener"/>. Ordering, ownership, backpressure, and close
    /// semantics are identical to Transport.Tcp. Uses: a client host's host player connecting to
    /// itself, and server E2E tests without real TCP.
    /// </summary>
    public sealed class InProcessTransport
    {
        private static long s_nextConnectionId; // Process-wide monotonic — IConnection.Id contract.

        /// <summary>
        /// Constructs the in-process transport pair.
        /// </summary>
        /// <param name="maxQueuedPacketsPerConnection">Receive inbox capacity per endpoint. When
        /// full, senders wait (equivalent to TCP socket-buffer backpressure).</param>
        /// <param name="logger">Optional logger. Only low-frequency paths are logged, such as a
        /// failing server OnConnected.</param>
        public InProcessTransport(int maxQueuedPacketsPerConnection = 256, ILogger? logger = null)
        {
            if (maxQueuedPacketsPerConnection <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxQueuedPacketsPerConnection));
            }

            var listener = new InProcessListener();
            Listener = listener;
            Connector = new InProcessConnector(
                listener, maxQueuedPacketsPerConnection, new SafeLogger(logger ?? NullLogger.Instance));
        }

        /// <summary>Accepting side. StartAsync(serverHandler) opens acceptance; Connect fails after StopAsync.</summary>
        public ITransportListener Listener { get; }

        /// <summary>Connecting side. Each ConnectAsync(clientHandler) call creates a new connection pair.</summary>
        /// <remarks>ConnectAsync runs the server handler's OnConnected synchronously on the
        /// calling thread (deterministic ordering). If the server OnConnected blocks, ConnectAsync
        /// blocks too, so OnConnected must return quickly as the contract requires.</remarks>
        public IConnector Connector { get; }

        internal static long NextConnectionId() => Interlocked.Increment(ref s_nextConnectionId);
    }
}
