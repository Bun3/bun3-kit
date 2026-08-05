using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Transport.Tcp
{
    /// <summary>TCP 나가는 연결. 수신 프레이밍/생명주기는 서버와 같은 TcpConnection을 재사용한다.</summary>
    public sealed class TcpConnector : IConnector
    {
        private readonly TcpConnectorOptions _options;
        private readonly ILogger _logger;
        private long _nextConnectionId;

        /// <summary>커넥터를 구성한다. ConnectAsync 전까지는 소켓을 열지 않는다.</summary>
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
                // ns2.1의 ConnectAsync에는 ct 오버로드가 없다 — 취소 시 소켓을 닫아 깨운다.
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
                // 계약: OnConnected 반환 전에는 OnPacket/OnClosed가 발생하지 않도록
                // 수신 루프는 OnConnected 이후에 시작한다.
                handler.OnConnected(connection);
                _ = Task.Run(connection.RunReceiveLoopAsync);
            }
            catch
            {
                // OnConnected가 던지면 핸들러가 이 연결을 등록하지 못한 것 —
                // OnClosed 없이 소켓만 정리하고 호출자에게 원본 예외를 전파한다.
                connection.Close();
                throw;
            }

            return connection;
        }
    }
}
