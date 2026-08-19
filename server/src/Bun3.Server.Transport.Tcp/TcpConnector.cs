using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Transport.Tcp
{
    /// <summary>Outgoing TCP connection. Reuses the same TcpConnection as the server for receive framing/lifecycle.</summary>
    public sealed class TcpConnector : IConnector
    {
        private readonly TcpConnectorOptions _options;
        private readonly ILogger _logger;
        private long _nextConnectionId;

        /// <summary>Constructs the connector. No socket is opened until ConnectAsync.</summary>
        public TcpConnector(TcpConnectorOptions options, ILogger? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
        }

        /// <inheritdoc />
        public async ValueTask<IConnection> ConnectAsync(IConnectionHandler handler, CancellationToken ct = default)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            ct.ThrowIfCancellationRequested();
            var client = new TcpClient();
            try
            {
                // netstandard2.1 ConnectAsync has no ct overload — wake it on cancel by closing the socket.
                using (ct.Register(() => client.Close()))
                {
                    await client.ConnectAsync(_options.Host, _options.Port).ConfigureAwait(false);
                }
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                client.Dispose();
                throw new OperationCanceledException(ct);
            }
            catch
            {
                client.Dispose();
                throw;
            }

            client.NoDelay = true;
            var connection = new TcpConnection(
                Interlocked.Increment(ref _nextConnectionId),
                client,
                new TcpTransportOptions { MaxPacketSize = _options.MaxPacketSize },
                handler,
                _logger);

            try
            {
                // Contract: the receive loop starts after OnConnected so no OnPacket/OnClosed
                // occurs before OnConnected returns.
                handler.OnConnected(connection);
                _ = Task.Run(connection.RunReceiveLoopAsync);
            }
            catch
            {
                // If OnConnected throws, the handler never registered this connection —
                // clean up the socket without OnClosed and propagate the original exception.
                connection.Close();
                throw;
            }

            return connection;
        }
    }
}
