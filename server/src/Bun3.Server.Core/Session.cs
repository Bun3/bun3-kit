using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Core
{
    /// <summary>
    /// Server-side counterpart of one connection (shares its lifetime). Packets accumulate in a
    /// per-session queue consumed by a single loop in order, so handlers of one session never run
    /// concurrently.
    /// </summary>
    public abstract class Session
    {
        private readonly ConcurrentQueue<object> _inbox = new ConcurrentQueue<object>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private ILogger _logger = NullLogger.Instance;
        private int _maxQueuedPackets = 256;
        private TimeSpan _slowWorkWarning = TimeSpan.FromSeconds(1);
        private volatile bool _closed;
        private Exception? _closeError;
        private int _queuedCount;

        /// <summary>Creates a session bound to the given connection.</summary>
        protected Session(IConnection connection)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>Session identifier; same as the connection identifier.</summary>
        public long Id => Connection.Id;

        /// <summary>Connection this session is bound to.</summary>
        public IConnection Connection { get; }

        /// <summary>Called once when the connection is established and the consume loop starts.</summary>
        protected virtual ValueTask OnConnectedAsync() => default;

        /// <summary>Handles one packet. Never runs concurrently within the same session.</summary>
        protected abstract ValueTask OnPacketAsync(ReadOnlyMemory<byte> packet);

        /// <summary>Called once when the session ends. error is null on a clean close.</summary>
        protected virtual ValueTask OnDisconnectedAsync(Exception? error) => default;

        /// <summary>
        /// Policy for exceptions thrown by OnConnectedAsync/OnPacketAsync. Defaults to closing the
        /// session. Override only when the game knows the exception is safe to ignore.
        /// </summary>
        protected virtual ErrorDecision OnHandlerError(Exception ex) => ErrorDecision.CloseSession;

        /// <summary>Sends one packet over this session's connection.</summary>
        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default) =>
            Connection.SendAsync(packet, ct);

        /// <summary>Server-initiated disconnect. The session is cleaned up via the transport's OnClosed notification.</summary>
        public void Kick() => Connection.Close();

        /// <summary>Disconnects with a reason code. Core does not know the wire format, so the base
        /// behaves like a reasonless kick — the Rpc layer (RpcSession) overrides this to send
        /// Disconnect best-effort.</summary>
        public virtual void Kick(int reasonCode) => Kick();

        /// <summary>
        /// Posts work onto the session actor queue — it runs sequentially in the same lane as
        /// packet handling, so it may touch handler state without locks. Returns false (work not
        /// run) if the session is closed or the queue is at its cap. Unhandled exceptions from the
        /// work are logged and the session keeps running. A race near shutdown may return true yet
        /// never run the work (best effort).
        /// </summary>
        public bool Post(Func<ValueTask> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (_closed)
            {
                return false;
            }

            if (Interlocked.Increment(ref _queuedCount) > _maxQueuedPackets)
            {
                Interlocked.Decrement(ref _queuedCount);
                return false;   // Unlike packet overflow this does not kick — the caller decides how to handle the skip.
            }

            _inbox.Enqueue(work);
            _signal.Release();
            return true;
        }

        internal void Initialize(ILogger logger, int maxQueuedPackets, TimeSpan slowWorkWarning)
        {
            _logger = logger;
            _maxQueuedPackets = maxQueuedPackets;
            _slowWorkWarning = slowWorkWarning;
        }

        internal void EnqueuePacket(byte[] packet)
        {
            if (_closed)
            {
                return;
            }

            if (Interlocked.Increment(ref _queuedCount) > _maxQueuedPackets)
            {
                Interlocked.Decrement(ref _queuedCount);
                _logger.LogWarning(
                    "Session {SessionId}: inbox overflow (>{MaxQueuedPackets}); kicking.", Id, _maxQueuedPackets);
                Kick(DisconnectCode.QueueOverflow);
                return;
            }

            _inbox.Enqueue(packet); // Ownership-transfer contract (IConnectionHandler) — queued as-is, no copy.
            _signal.Release();
        }

        internal void NotifyClosed(Exception? error)
        {
            _closeError = error;
            _closed = true;
            _signal.Release(); // Wake the consume loop so it can exit.
        }

        internal async Task RunAsync()
        {
            try
            {
                try
                {
                    await OnConnectedAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    HandleError(ex);
                }

                while (true)
                {
                    await _signal.WaitAsync().ConfigureAwait(false);
                    if (_closed)
                    {
                        break; // Remaining items (packets, posted work) are not processed after close.
                    }

                    var dequeued = _inbox.TryDequeue(out var item);
                    System.Diagnostics.Debug.Assert(dequeued, "signal/inbox invariant broken");

                    Interlocked.Decrement(ref _queuedCount);
                    if (item is byte[] packet)
                    {
                        try
                        {
                            await WatchAsync(OnPacketAsync(packet), "handler").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            HandleError(ex);
                        }
                    }
                    else
                    {
                        var work = (Func<ValueTask>)item!;
                        try
                        {
                            await WatchAsync(work(), "posted work").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Session {SessionId}: posted work threw; session continues.", Id);
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    await OnDisconnectedAsync(_closeError).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Session {SessionId}: OnDisconnectedAsync threw.", Id);
                }
            }
        }

        // Takes the in-flight ValueTask directly — allocation-free fast path with no closure at the call site.
        private async ValueTask WatchAsync(ValueTask pending, string kind)
        {
            if (pending.IsCompleted || _slowWorkWarning <= TimeSpan.Zero)
            {
                await pending.ConfigureAwait(false);   // Completed synchronously or watching disabled — skip the watch machinery.
                return;
            }

            var task = pending.AsTask();
            using var delayCts = new CancellationTokenSource();
            var delay = Task.Delay(_slowWorkWarning, delayCts.Token);
            if (await Task.WhenAny(task, delay).ConfigureAwait(false) != task)
            {
                _logger.LogWarning(
                    "Session {SessionId}: {Kind} running longer than {Threshold} — queue is blocked.",
                    Id, kind, _slowWorkWarning);
            }

            delayCts.Cancel();   // Dispose the timer promptly (avoids timer buildup under load).
            await task.ConfigureAwait(false);
        }

        private void HandleError(Exception ex)
        {
            ErrorDecision decision;
            try
            {
                decision = OnHandlerError(ex);
            }
            catch (Exception hookEx)
            {
                _logger.LogError(hookEx, "Session {SessionId}: OnHandlerError threw.", Id);
                decision = ErrorDecision.CloseSession;
            }

            if (decision == ErrorDecision.CloseSession)
            {
                _logger.LogError(ex, "Session {SessionId}: handler exception; closing session.", Id);
                Kick();
            }
            else
            {
                _logger.LogWarning(ex, "Session {SessionId}: handler exception ignored by OnHandlerError.", Id);
            }
        }
    }
}
