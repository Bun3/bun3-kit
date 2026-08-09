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
    /// <summary>순수 Socket 기반 TCP 리스너. 프레이밍은 PacketFormat(4바이트 길이 프리픽스).</summary>
    public sealed class TcpTransportListener : ITransportListener
    {
        private readonly TcpTransportOptions _options;
        private readonly ILogger _logger;
        private TcpListener? _listener;
        private Task? _acceptLoop;
        private long _nextConnectionId;
        private int? _boundPort;
        private volatile bool _stopping;
        private int _activeConnections;
        private volatile bool _capacityLogged;

        /// <summary>TCP 리스너를 구성한다. StartAsync 전까지는 바인딩하지 않는다.</summary>
        public TcpTransportListener(TcpTransportOptions options, ILogger? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
        }

        /// <summary>실제 바인딩된 포트. Options.Port가 0이면 시작 후 여기서 확인한다. Stop 이후에도 유효.</summary>
        public int? BoundPort => _boundPort;

        /// <remarks>단일 사용: StopAsync 이후 재시작할 수 없다. 새 인스턴스를 생성할 것.</remarks>
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
            _boundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _logger.LogInformation("TCP listening on port {Port}.", BoundPort);
            _acceptLoop = Task.Run(() => AcceptLoopAsync(handler));
            return Task.CompletedTask;
        }

        /// <summary>신규 연결 수락을 중단하고 accept 루프가 끝나기를 기다린다.</summary>
        public async Task StopAsync(CancellationToken ct = default)
        {
            _stopping = true;
            _listener?.Stop(); // AcceptTcpClientAsync를 깨운다
            if (_acceptLoop != null)
            {
                await _acceptLoop.ConfigureAwait(false);
            }
        }

        private async Task AcceptLoopAsync(IConnectionHandler handler)
        {
            var listener = _listener!;
            var counted = new CountingHandler(this, handler);   // OnClosed에서 활성 연결 수를 회수한다
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception) when (_stopping)
                {
                    break; // Stop()에 의한 정상 종료
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Accept failed.");
                    await Task.Delay(100).ConfigureAwait(false); // 지속 실패 시 핫스핀 방지
                    continue;
                }

                // 연결 수 상한 — accept 루프는 단일 스레드이므로 검사·증가에 경합이 없다.
                if (_options.MaxConnections > 0
                    && Volatile.Read(ref _activeConnections) >= _options.MaxConnections)
                {
                    if (!_capacityLogged)
                    {
                        _capacityLogged = true;   // 상한 도달당 1회만 경고 — 거부 폭주가 로그를 뒤덮지 않게
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

                    // 계약: OnConnected 반환 전에는 OnPacket/OnClosed가 발생하지 않도록
                    // 수신 루프는 OnConnected 이후에 시작한다.
                    counted.OnConnected(connection);
                    _ = Task.Run(connection.RunReceiveLoopAsync);
                }
                catch (Exception ex)
                {
                    // OnConnected가 던지면 핸들러가 이 연결을 등록하지 못한 것이므로
                    // OnClosed를 통지하지 않고 소켓만 정리한다 (exactly-once는 OnConnected가
                    // 정상 반환한 연결에 대한 계약). OnClosed가 오지 않으므로 카운트도 여기서 회수.
                    OnConnectionClosed();
                    _logger.LogError(ex, "Connection setup failed; closing client.");
                    try { client.Close(); } catch { }
                }
            }
        }

        private void OnConnectionClosed()
        {
            Interlocked.Decrement(ref _activeConnections);
            _capacityLogged = false;   // 다음에 다시 상한에 닿으면 재경고
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
