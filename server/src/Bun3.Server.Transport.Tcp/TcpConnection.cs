using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Common.Network;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Transport.Tcp
{
    internal sealed class TcpConnection : IConnection
    {
        // 그레이스풀 종료 유예 상한 — 로컬호스트에서 잔여 수신 바이트를 드레인하기엔 충분히 크고,
        // 비협조적인 원격(수신 루프 미사용 상대 등)에서도 종료가 이 이상 지연되지 않도록 상한을 둔다.
        private static readonly TimeSpan GracefulCloseGrace = TimeSpan.FromMilliseconds(200);

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly TcpTransportOptions _options;
        private readonly IConnectionHandler _handler;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private int _closed; // 0 = open, 1 = closed
        private Exception? _closeError;
        private volatile bool _receiveLoopStarted;

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
                // 아직 읽지 않은 수신 데이터가 남은 채로 소켓을 바로 닫으면 OS가 RST를 보내
                // 방금 보낸 데이터(예: Kick의 Disconnect)까지 유실시킬 수 있다(Windows WSAECONNRESET 등).
                // 송신 방향만 먼저 셧다운(FIN)해 상대가 마지막 데이터를 받게 하고, 수신 루프가
                // 남은 바이트를 드레인할 짧은 유예를 준 뒤 실제 소켓을 정리한다.
                _client.Client.Shutdown(SocketShutdown.Send);
            }
            catch
            {
                // 이미 끊겼거나 셧다운 불가 — 바로 정리해도 안전하다
                DisposeSocket();
                return;
            }

            if (!_receiveLoopStarted)
            {
                // 수신 루프가 시작되지 않았다(연결 설정 실패 등) — 그 루프의 finally가
                // 정리를 대신할 수 없으므로 여기서 직접 소켓을 해제한다.
                DisposeSocket();
                return;
            }

            _ = Task.Delay(GracefulCloseGrace).ContinueWith(
                _ => DisposeSocket(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void DisposeSocket()
        {
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
            _receiveLoopStarted = true;
            Exception? error = null;
            try
            {
                while (true)
                {
                    var packet = await PacketFormat.ReadPacketAsync(_stream, _options.MaxPacketSize)
                        .ConfigureAwait(false);
                    if (packet == null)
                    {
                        break; // 원격의 깨끗한 종료(또는 로컬 셧다운에 대한 응답)
                    }

                    if (IsOpen)
                    {
                        _handler.OnPacket(this, packet);   // half-close 유예 중(드레인)에는 전달하지 않는다
                    }
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
                // 루프가 끝났다는 것 자체가 "더 드레인할 것이 없다"는 신호 — 유예 없이 바로 정리한다.
                Close();          // _closed 플래그 보장(아직 안 닫혔다면 셧다운 시도, 이미 닫혔다면 no-op)
                DisposeSocket();
                _handler.OnClosed(this, error ?? Volatile.Read(ref _closeError));
            }
        }
    }
}
