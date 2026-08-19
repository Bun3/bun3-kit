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
        // Graceful-close grace cap — large enough to drain remaining inbound bytes on localhost,
        // yet bounds the close delay against an uncooperative remote (e.g. one not running a
        // receive loop).
        private static readonly TimeSpan GracefulCloseGrace = TimeSpan.FromMilliseconds(200);

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly TcpTransportOptions _options;
        private readonly IConnectionHandler _handler;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        // Header scratch buffers — sends are serialized by _sendLock, receives by the single loop, so reuse is safe.
        private readonly byte[] _sendHeader = new byte[PacketFormat.HeaderSize];
        private readonly byte[] _receiveHeader = new byte[PacketFormat.HeaderSize];
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
                return; // Contract: sending on a closed connection is a no-op.
            }

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!IsOpen)
                {
                    return;
                }

                await PacketFormat.WritePacketAsync(_stream, packet, _sendHeader, ct).ConfigureAwait(false);
            }
            catch (Exception) when (!IsOpen)
            {
                // Raced with a local Close mid-send — no-op per contract.
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
                // Closing the socket outright while unread inbound data remains can make the OS
                // send RST, losing data just sent (e.g. Kick's Disconnect) — Windows
                // WSAECONNRESET etc. Shut down only the send side first (FIN) so the peer gets
                // the last data, give the receive loop a short grace to drain remaining bytes,
                // then dispose the actual socket.
                _client.Client.Shutdown(SocketShutdown.Send);
            }
            catch
            {
                // Already disconnected or shutdown impossible — safe to dispose right away.
                DisposeSocket();
                return;
            }

            if (!_receiveLoopStarted)
            {
                // The receive loop never started (e.g. connection setup failed) — its finally
                // cannot do the cleanup, so release the socket here directly.
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
                _client.Close(); // Wakes the receive loop's Read, which leads to the OnClosed notification.
            }
            catch
            {
                // Ignore exceptions while disposing the socket.
            }
        }

        /// <summary>
        /// Receive loop. Runs once per connection and reports OnClosed exactly once on exit.
        /// </summary>
        internal async Task RunReceiveLoopAsync()
        {
            _receiveLoopStarted = true;
            Exception? error = null;
            try
            {
                while (true)
                {
                    var packet = await PacketFormat.ReadPacketAsync(_stream, _options.MaxPacketSize, _receiveHeader)
                        .ConfigureAwait(false);
                    if (packet == null)
                    {
                        break; // Clean close by the remote (or its response to our local shutdown).
                    }

                    if (IsOpen)
                    {
                        _handler.OnPacket(this, packet);   // Not delivered during the half-close grace (drain).
                    }
                }
            }
            catch (Exception) when (!IsOpen)
            {
                // Local Close() woke the Read — treat as a clean close (error = null).
            }
            catch (Exception ex)
            {
                error = ex; // InvalidDataException (packet too large), IOException (reset), etc.
            }
            finally
            {
                // The loop ending is itself the signal that there is nothing left to drain — skip
                // the grace (Close's 200ms deferred cleanup), just settle the flag and dispose now.
                Interlocked.Exchange(ref _closed, 1);
                DisposeSocket();
                _handler.OnClosed(this, error ?? Volatile.Read(ref _closeError));
            }
        }
    }
}
