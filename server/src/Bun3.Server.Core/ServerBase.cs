using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Core
{
    /// <summary>
    /// Server base that manages connection-to-session binding and lifecycle on top of a transport
    /// listener. The single coupling point with game code is the CreateSession factory.
    /// </summary>
    public abstract class ServerBase<TSession> where TSession : Session
    {
        private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

        private readonly ITransportListener _transport;
        private readonly ILogger _logger;
        private readonly int _maxQueuedPackets;
        private readonly TimeSpan _slowWorkWarning;
        private readonly ConcurrentDictionary<long, SessionEntry> _sessions =
            new ConcurrentDictionary<long, SessionEntry>();
        private readonly Handler _handler;
        private volatile bool _running;

        /// <summary>Constructs the server base. The transport is bound to the handler at start.
        /// slowWorkWarning: log a warning when a session queue item exceeds this duration
        /// (null = 1 second, zero or less = off).</summary>
        protected ServerBase(
            ITransportListener transport,
            ILogger? logger = null,
            int maxQueuedPackets = 256,
            TimeSpan? slowWorkWarning = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
            _maxQueuedPackets = maxQueuedPackets;
            _slowWorkWarning = slowWorkWarning ?? TimeSpan.FromSeconds(1);
            _handler = new Handler(this);
        }

        /// <summary>True after StartAsync until StopAsync.</summary>
        public bool IsRunning => _running;

        /// <summary>Snapshot of currently connected sessions.</summary>
        public IReadOnlyCollection<TSession> Sessions =>
            _sessions.Values.Select(e => e.Session).ToArray();

        /// <summary>Creates the session instance for a new connection. The only coupling point with game code.</summary>
        protected abstract TSession CreateSession(IConnection connection);

        /// <remarks>Single use: cannot be restarted after StopAsync. Create a new instance instead.</remarks>
        public async Task StartAsync(CancellationToken ct = default)
        {
            // Set the flag first so connections arriving right after transport start are not kicked.
            _running = true;
            try
            {
                await _transport.StartAsync(_handler, ct).ConfigureAwait(false);
            }
            catch
            {
                _running = false;
                throw;
            }

            _logger.LogInformation("Server started.");
        }

        /// <summary>
        /// Stops accepting new connections, kicks all sessions, then waits up to drainTimeout for
        /// the consume loops to finish.
        /// </summary>
        public async Task StopAsync(TimeSpan? drainTimeout = null, CancellationToken ct = default)
        {
            _running = false;
            await _transport.StopAsync(ct).ConfigureAwait(false);

            var entries = _sessions.Values.ToArray();
            foreach (var entry in entries)
            {
                entry.Session.Kick(DisconnectCode.ServerShutdown);
            }

            var drain = Task.WhenAll(entries.Select(e => e.Completion));
            var timeout = drainTimeout ?? DefaultDrainTimeout;
            using (var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var finished = await Task.WhenAny(drain, Task.Delay(timeout, delayCts.Token)).ConfigureAwait(false);
                if (finished == drain)
                {
                    delayCts.Cancel(); // Drain done — dispose the leftover timer promptly.
                    await drain.ConfigureAwait(false); // Observe the result (Completion never faults, but for discipline).
                }
                else
                {
                    _logger.LogWarning(
                        "Server stop: {SessionCount} session(s) did not drain within {Timeout}.",
                        entries.Length, timeout);
                }
            }

            _logger.LogInformation("Server stopped.");
        }

        private void HandleConnected(IConnection connection)
        {
            if (!_running)
            {
                _logger.LogDebug(
                    "Connection {ConnectionId} arrived while server is not running; closing.", connection.Id);
                connection.Close();
                return;
            }

            TSession session;
            try
            {
                session = CreateSession(connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateSession failed for connection {ConnectionId}; closing.", connection.Id);
                connection.Close();
                return;
            }

            session.Initialize(_logger, _maxQueuedPackets, _slowWorkWarning);
            var entry = new SessionEntry(session);
            if (!_sessions.TryAdd(connection.Id, entry))
            {
                // Transport contract violation (duplicate id) — keep the existing session and close
                // only the new connection. The session was never bound to RunAsync, so it is
                // discarded without lifecycle callbacks.
                _logger.LogError(
                    "Duplicate connection id {ConnectionId} from transport; closing new connection.", connection.Id);
                connection.Close();
                return;
            }

            entry.BindRunTask(session.RunAsync());
        }

        private void HandlePacket(IConnection connection, byte[] packet)
        {
            if (TryGetOwnedEntry(connection, out var entry))
            {
                entry.Session.EnqueuePacket(packet);
            }
            else if (_logger.IsEnabled(LogLevel.Debug))   // Packet path — avoid argument boxing when the level is off.
            {
                // Can happen legitimately when racing stop/kick, so log at Debug only.
                _logger.LogDebug("Packet from unknown connection {ConnectionId}; dropped.", connection.Id);
            }
        }

        private void HandleClosed(IConnection connection, Exception? error)
        {
            // netstandard2.1 lacks TryRemove(KeyValuePair), so use ICollection.Remove for an
            // atomic value-matched conditional removal.
            if (TryGetOwnedEntry(connection, out var entry)
                && ((ICollection<KeyValuePair<long, SessionEntry>>)_sessions).Remove(
                    new KeyValuePair<long, SessionEntry>(connection.Id, entry)))
            {
                entry.Session.NotifyClosed(error);
            }
        }

        /// <summary>Finds the session entry owned by this connection only — reference identity
        /// filters out stray frames/OnClosed from a connection rejected for a duplicate id, so
        /// they cannot hit the original session with the same id.</summary>
        private bool TryGetOwnedEntry(IConnection connection, out SessionEntry entry) =>
            _sessions.TryGetValue(connection.Id, out entry)
            && ReferenceEquals(entry.Session.Connection, connection);

        private sealed class SessionEntry
        {
            public readonly TSession Session;
            private readonly TaskCompletionSource<bool> _completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public SessionEntry(TSession session)
            {
                Session = session;
            }

            /// <summary>Completes when the actual RunAsync task ends. Never completes before
            /// binding, so StopAsync observing an entry between registration and binding still
            /// waits for the drain.</summary>
            public Task Completion => _completion.Task;

            public void BindRunTask(Task runTask)
            {
                runTask.ContinueWith(
                    _ => _completion.TrySetResult(true),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        private sealed class Handler : IConnectionHandler
        {
            private readonly ServerBase<TSession> _server;

            public Handler(ServerBase<TSession> server) => _server = server;

            public void OnConnected(IConnection connection) => _server.HandleConnected(connection);

            public void OnPacket(IConnection connection, byte[] packet) =>
                _server.HandlePacket(connection, packet);

            public void OnClosed(IConnection connection, Exception? error) =>
                _server.HandleClosed(connection, error);
        }
    }
}
