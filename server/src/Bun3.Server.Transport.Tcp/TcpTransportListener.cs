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

            _listener = new TcpListener(IPAddress.Any, _options.Port);
            _listener.Start(_options.Backlog);
            _boundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _logger.LogInformation("TCP listening on port {Port}.", BoundPort);
            _acceptLoop = Task.Run(() => AcceptLoopAsync(handler));
            return Task.CompletedTask;
        }

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

                try
                {
                    client.NoDelay = true;
                    var connection = new TcpConnection(
                        Interlocked.Increment(ref _nextConnectionId), client, _options, handler, _logger);

                    // 계약: OnConnected 반환 전에는 OnPacket/OnClosed가 발생하지 않도록
                    // 수신 루프는 OnConnected 이후에 시작한다.
                    handler.OnConnected(connection);
                    _ = Task.Run(connection.RunReceiveLoopAsync);
                }
                catch (Exception ex)
                {
                    // OnConnected가 던지면 핸들러가 이 연결을 등록하지 못한 것이므로
                    // OnClosed를 통지하지 않고 소켓만 정리한다 (exactly-once는 OnConnected가
                    // 정상 반환한 연결에 대한 계약).
                    _logger.LogError(ex, "Connection setup failed; closing client.");
                    try { client.Close(); } catch { }
                }
            }
        }
    }
}
