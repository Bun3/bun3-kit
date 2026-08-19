using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Transport.InProcess
{
    /// <summary>In-process connector that attaches to the listener of the same InProcessTransport.</summary>
    internal sealed class InProcessConnector : IConnector
    {
        private readonly InProcessListener _listener;
        private readonly int _maxQueuedPackets;
        private readonly ILogger _logger;

        internal InProcessConnector(InProcessListener listener, int maxQueuedPackets, ILogger logger)
        {
            _listener = listener;
            _maxQueuedPackets = maxQueuedPackets;
            _logger = logger;
        }

        /// <inheritdoc />
        public ValueTask<IConnection> ConnectAsync(IConnectionHandler handler, CancellationToken ct = default)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            ct.ThrowIfCancellationRequested();
            var serverHandler = _listener.TryGetAcceptHandler();
            if (serverHandler == null)
            {
                throw new InvalidOperationException(
                    "InProcess listener is not accepting connections (not started or already stopped).");
            }

            var client = new InProcessConnection(InProcessTransport.NextConnectionId(), _maxQueuedPackets, handler);
            var server = new InProcessConnection(InProcessTransport.NextConnectionId(), _maxQueuedPackets, serverHandler);
            client.Link(server);
            server.Link(client);

            // Server-side accept — same as the TCP listener: per contract, the receive pump starts
            // only after OnConnected returns.
            try
            {
                serverHandler.OnConnected(server);
                _ = Task.Run(server.RunReceivePumpAsync);
            }
            catch (Exception ex)
            {
                // If OnConnected throws, the handler never registered this connection — close only
                // the server endpoint, without a server OnClosed. The client observes
                // OnClosed(null) right after a normal connect (matches TCP behavior on a remote
                // rejection).
                _logger.LogError(ex, "InProcess server OnConnected failed; closing server endpoint.");
                server.Close();
            }

            try
            {
                handler.OnConnected(client);
                _ = Task.Run(client.RunReceivePumpAsync);
            }
            catch
            {
                // Same as TcpConnector: close the pair without a client OnClosed (the server
                // receives OnClosed(null)) and propagate the original exception to the caller.
                client.Close();
                throw;
            }

            return new ValueTask<IConnection>(client);
        }
    }
}
