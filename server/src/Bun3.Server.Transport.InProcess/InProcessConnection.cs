using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Transport.InProcess
{
    internal sealed class InProcessConnection : IConnection
    {
        // Close (FIN) sentinel — compared by reference only. User packets are always freshly
        // copied arrays (empty packets use Array.Empty), so they can never collide with this
        // instance.
        private static readonly byte[] ClosedByPeerSentinel = new byte[1];

        // Amount released into the slot semaphore on close to wake every sender blocked on
        // backpressure. Far larger than any realistic number of concurrent waiters, and each
        // semaphore is released at most twice (own Close + peer Close, both CAS-guarded), so it
        // cannot overflow.
        private const int WakeAllSenders = 1 << 20;

        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _items = new SemaphoreSlim(0);
        private readonly SemaphoreSlim _slots; // Bounded inbox — equivalent to TCP socket-buffer backpressure.
        private readonly IConnectionHandler _handler;
        private InProcessConnection _peer = null!; // Always set via Link right after the pair is created.
        private int _closed; // 0 = open, 1 = closed

        internal InProcessConnection(long id, int maxQueuedPackets, IConnectionHandler handler)
        {
            Id = id;
            _handler = handler;
            _slots = new SemaphoreSlim(maxQueuedPackets);
        }

        public long Id { get; }

        public string? RemoteAddress => "inproc";

        public bool IsOpen => Volatile.Read(ref _closed) == 0;

        internal void Link(InProcessConnection peer) => _peer = peer;

        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default)
        {
            if (!IsOpen)
            {
                return default; // Contract: sending on a closed connection is a no-op.
            }

            return _peer.EnqueueFromPeerAsync(packet, this, ct);
        }

        private async ValueTask EnqueueFromPeerAsync(
            ReadOnlyMemory<byte> packet, InProcessConnection sender, CancellationToken ct)
        {
            if (!IsOpen)
            {
                return; // Receiver already closed — drop, like bytes vanishing into a closed socket.
            }

            await _slots.WaitAsync(ct).ConfigureAwait(false);
            if (!IsOpen || !sender.IsOpen)
            {
                return; // Either side closed while waiting (including a WakeAllSenders wake-up) — drop per the no-op contract.
            }

            // Ownership contract: OnPacket's array becomes receiver-owned, so copy the sender's
            // buffer exactly once here. (TCP also allocates one new array per received packet, so
            // allocations per packet are identical.)
            _inbox.Enqueue(packet.ToArray());
            _items.Release();
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return; // Idempotent.
            }

            _items.Release();               // Wake our own pump so it exits and reports OnClosed.
            _slots.Release(WakeAllSenders); // Wake every peer sender blocked on this inbox.
            // Also wake our senders blocked on the peer's inbox — equivalent to TCP's local Close
            // waking a blocked write via dispose so it returns as a no-op. Woken senders see the
            // closure via sender.IsOpen and drop. Even if the peer stays alive, all our later
            // sends are filtered by IsOpen, so the inflated slot count is harmless.
            _peer._slots.Release(WakeAllSenders);
            _peer.NotifyPeerClosed();
        }

        private void NotifyPeerClosed()
        {
            // Equivalent to FIN — enqueued at the tail so earlier packets are all delivered (drained) before the close.
            _inbox.Enqueue(ClosedByPeerSentinel);
            _items.Release();
        }

        /// <summary>
        /// Receive pump. One per connection; starts only after that endpoint's OnConnected has
        /// returned. Reports OnClosed exactly once on exit (this finally is the sole notification
        /// point).
        /// </summary>
        internal async Task RunReceivePumpAsync()
        {
            Exception? error = null;
            try
            {
                while (true)
                {
                    await _items.WaitAsync().ConfigureAwait(false);
                    if (!IsOpen)
                    {
                        break; // Local Close — discard whatever is still queued (same as TCP's local Close).
                    }

                    if (!_inbox.TryDequeue(out var packet))
                    {
                        continue;
                    }

                    if (ReferenceEquals(packet, ClosedByPeerSentinel))
                    {
                        break; // Peer closed — drain complete.
                    }

                    _slots.Release();
                    _handler.OnPacket(this, packet); // Array ownership transfers — never touch this array afterwards.
                }
            }
            catch (Exception ex)
            {
                error = ex; // OnPacket threw — exit with error, same as the TCP receive loop.
            }
            finally
            {
                Close(); // Includes notifying the peer (idempotent no-op if already closed).
                _handler.OnClosed(this, error);
            }
        }
    }
}
