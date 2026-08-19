using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Transport.Tcp
{
    /// <summary>Plain Socket-based TCP listener. Framing is PacketFormat (4-byte length prefix).</summary>
    public sealed class TcpTransportListener : ITransportListener
    {
        private readonly TcpTransportOptions _options;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();
        private TcpListener? _listener;
        private Task? _acceptLoop;
        private long _nextConnectionId;
        private int _boundPort = -1; // int? can tear — sentinel int + Volatile guarantees atomicity.
        private volatile bool _stopping;
        private int _activeConnections;
        private volatile bool _capacityLogged;

        /// <summary>Constructs the TCP listener. Does not bind until StartAsync.</summary>
        public TcpTransportListener(TcpTransportOptions options, ILogger? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
        }

        /// <summary>The actually bound port. If Options.Port is 0, read it here after start. Remains valid after Stop.</summary>
        public int? BoundPort
        {
            get
            {
                var port = Volatile.Read(ref _boundPort);
                return port < 0 ? (int?)null : port;
            }
        }

        /// <remarks>Single use: cannot be restarted after StopAsync. Create a new instance instead.</remarks>
        public Task StartAsync(IConnectionHandler handler, CancellationToken ct = default)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (_listener != null)
            {
                throw new InvalidOperationException("Listener is already started.");
            }

            _listener = new TcpListener(_options.BindAddress ?? IPAddress.Any, _options.Port);
            _listener.Start(_options.Backlog);
            Volatile.Write(ref _boundPort, ((IPEndPoint)_listener.LocalEndpoint).Port);
            _logger.LogInformation("TCP listening on port {Port}.", BoundPort);
            _acceptLoop = Task.Run(() => AcceptLoopAsync(handler));
            return Task.CompletedTask;
        }

        /// <summary>Stops accepting new connections and waits for the accept loop to finish.</summary>
        public async Task StopAsync(CancellationToken ct = default)
        {
            _stopping = true;
            _stopCts.Cancel(); // Wakes immediately even if waiting in the accept-failure backoff (100ms).
            _listener?.Stop(); // Wakes AcceptTcpClientAsync.
            if (_acceptLoop != null)
            {
                await _acceptLoop.ConfigureAwait(false);
            }
        }

        private async Task AcceptLoopAsync(IConnectionHandler handler)
        {
            var listener = _listener!;
            var counted = new CountingHandler(this, handler);   // Reclaims the active connection count in OnClosed.
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception) when (_stopping)
                {
                    break; // Normal shutdown via Stop().
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Accept failed.");
                    try
                    {
                        await Task.Delay(100, _stopCts.Token).ConfigureAwait(false); // Avoid hot spin on persistent failure.
                    }
                    catch (OperationCanceledException)
                    {
                        break; // StopAsync — exit immediately without waiting out the backoff.
                    }

                    continue;
                }

                // Connection cap — the accept loop is single-threaded, so check-then-increment has no race.
                if (_options.MaxConnections > 0
                    && Volatile.Read(ref _activeConnections) >= _options.MaxConnections)
                {
                    if (!_capacityLogged)
                    {
                        _capacityLogged = true;   // Warn once per time the cap is hit — a rejection burst must not flood the log.
                        _logger.LogWarning(
                            "Connection limit {MaxConnections} reached; rejecting new connections.",
                            _options.MaxConnections);
                    }

                    try { client.Close(); } catch { }
                    continue;
                }

                Interlocked.Increment(ref _activeConnections);
                try
                {
                    client.NoDelay = true;
                    var connection = new TcpConnection(
                        Interlocked.Increment(ref _nextConnectionId), client, _options, counted, _logger);

                    // Contract: the receive loop starts after OnConnected so no OnPacket/OnClosed
                    // occurs before OnConnected returns. The loop swallows exceptions internally,
                    // but OnClosed in its finally (handler code) may throw, so observe the fault
                    // and log it.
                    counted.OnConnected(connection);
                    var connectionId = connection.Id;
                    _ = Task.Run(connection.RunReceiveLoopAsync).ContinueWith(
                        t => _logger.LogError(t.Exception, "Connection {ConnectionId}: receive loop faulted.", connectionId),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
                }
                catch (Exception ex)
                {
                    // If OnConnected throws, the handler never registered this connection, so do
                    // not report OnClosed and only clean up the socket (exactly-once applies to
                    // connections whose OnConnected returned normally). No OnClosed will come, so
                    // reclaim the count here.
                    OnConnectionClosed();
                    _logger.LogError(ex, "Connection setup failed; closing client.");
                    try { client.Close(); } catch { }
                }
            }
        }

        private void OnConnectionClosed()
        {
            Interlocked.Decrement(ref _activeConnections);
            _capacityLogged = false;   // Warn again the next time the cap is reached.
        }

        private sealed class CountingHandler : IConnectionHandler
        {
            private readonly TcpTransportListener _listener;
            private readonly IConnectionHandler _inner;

            public CountingHandler(TcpTransportListener listener, IConnectionHandler inner)
            {
                _listener = listener;
                _inner = inner;
            }

            public void OnConnected(IConnection connection) => _inner.OnConnected(connection);

            public void OnPacket(IConnection connection, byte[] packet) => _inner.OnPacket(connection, packet);

            public void OnClosed(IConnection connection, Exception? error)
            {
                _listener.OnConnectionClosed();
                _inner.OnClosed(connection, error);
            }
        }
    }
}
