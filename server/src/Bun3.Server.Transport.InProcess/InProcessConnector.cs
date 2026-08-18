using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Transport.InProcess
{
    /// <summary>같은 InProcessTransport의 리스너로 붙는 인프로세스 커넥터.</summary>
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

            // 서버 측 수락 — Tcp 리스너와 동일: 계약상 OnConnected 반환 후에만 수신 펌프를 시작한다.
            try
            {
                serverHandler.OnConnected(server);
                _ = Task.Run(server.RunReceivePumpAsync);
            }
            catch (Exception ex)
            {
                // OnConnected가 던지면 핸들러가 이 연결을 등록하지 못한 것 — 서버 OnClosed 없이
                // 서버 끝점만 닫는다. 클라는 정상 연결 직후 OnClosed(null)를 관측한다(원격 거부의
                // TCP 동작과 동일).
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
                // TcpConnector와 동일: 클라 OnClosed 없이 페어를 닫고(서버는 OnClosed(null) 수신)
                // 원본 예외를 호출자에게 전파한다.
                client.Close();
                throw;
            }

            return new ValueTask<IConnection>(client);
        }
    }
}
