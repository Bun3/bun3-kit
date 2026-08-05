using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Transport.Tcp
{
    internal sealed class TcpConnection : IConnection
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly TcpTransportOptions _options;
        private readonly IConnectionHandler _handler;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private int _closed; // 0 = open, 1 = closed
        private Exception? _closeError;

        internal TcpConnection(
            long id,
            TcpClient client,
            TcpTransportOptions options,
            IConnectionHandler handler,
            ILogger logger)
        {
            Id = id;
            _client = client;
            _options = options;
            _handler = handler;
            _logger = logger;
            _stream = client.GetStream();
            RemoteAddress = client.Client.RemoteEndPoint?.ToString();
        }

        public long Id { get; }

        public string? RemoteAddress { get; }

        public bool IsOpen => Volatile.Read(ref _closed) == 0;

        public async ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default)
        {
            if (!IsOpen)
            {
                return; // 계약: 닫힌 연결에 대한 송신은 no-op
            }

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!IsOpen)
                {
                    return;
                }

                await PacketFormat.WritePacketAsync(_stream, packet, ct).ConfigureAwait(false);
            }
            catch (Exception) when (!IsOpen)
            {
                // 송신 도중 로컬 Close와 경합 — 계약상 no-op
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _closeError, ex, null);
                _logger.LogDebug(ex, "Connection {ConnectionId}: send failed; closing.", Id);
                Close();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }

            try
            {
                _client.Close(); // 수신 루프의 Read를 깨워 OnClosed 통지로 이어진다
            }
            catch
            {
                // 소켓 정리 중 예외는 무시
            }
        }

        /// <summary>
        /// 수신 루프. 연결당 1개 실행되며, 종료 시 OnClosed를 정확히 1회 통지한다.
        /// </summary>
        internal async Task RunReceiveLoopAsync()
        {
            Exception? error = null;
            try
            {
                while (true)
                {
                    var packet = await PacketFormat.ReadPacketAsync(_stream, _options.MaxPacketSize)
                        .ConfigureAwait(false);
                    if (packet == null)
                    {
                        break; // 원격의 깨끗한 종료
                    }

                    _handler.OnPacket(this, packet);
                }
            }
            catch (Exception) when (!IsOpen)
            {
                // 로컬 Close()가 Read를 깨운 경우 — 정상 종료로 취급 (error = null)
            }
            catch (Exception ex)
            {
                error = ex; // InvalidDataException(패킷 초과), IOException(리셋) 등
            }
            finally
            {
                Close();
                _handler.OnClosed(this, error ?? Volatile.Read(ref _closeError));
            }
        }
    }
}
