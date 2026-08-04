using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Core
{
    /// <summary>
    /// 전송 리스너 위에서 연결→세션 바인딩과 수명주기를 관리하는 서버 베이스.
    /// 게임 코드와의 결합점은 CreateSession 팩토리 하나다.
    /// </summary>
    public abstract class ServerBase<TSession> where TSession : Session
    {
        private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

        private readonly ITransportListener _transport;
        private readonly IBun3Logger _logger;
        private readonly SessionOptions _sessionOptions;
        private readonly ConcurrentDictionary<long, SessionEntry> _sessions =
            new ConcurrentDictionary<long, SessionEntry>();
        private readonly Handler _handler;
        private volatile bool _running;

        protected ServerBase(
            ITransportListener transport,
            IBun3Logger? logger = null,
            SessionOptions? sessionOptions = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? NullBun3Logger.Instance;
            _sessionOptions = sessionOptions ?? new SessionOptions();
            _handler = new Handler(this);
        }

        public bool IsRunning => _running;

        public IReadOnlyCollection<TSession> Sessions =>
            _sessions.Values.Select(e => e.Session).ToArray();

        protected abstract TSession CreateSession(IConnection connection);

        public async Task StartAsync(CancellationToken ct = default)
        {
            await _transport.StartAsync(_handler, ct).ConfigureAwait(false);
            _running = true;
            _logger.Log(Bun3LogLevel.Info, "Server started.");
        }

        /// <summary>
        /// 신규 수락을 중단하고 전 세션을 종료한 뒤, 소비 루프들이 끝나기를 drainTimeout까지 기다린다.
        /// </summary>
        public async Task StopAsync(TimeSpan? drainTimeout = null, CancellationToken ct = default)
        {
            _running = false;
            await _transport.StopAsync(ct).ConfigureAwait(false);

            var entries = _sessions.Values.ToArray();
            foreach (var entry in entries)
            {
                entry.Session.Kick();
            }

            var drain = Task.WhenAll(entries.Select(e => e.RunTask));
            var timeout = drainTimeout ?? DefaultDrainTimeout;
            var finished = await Task.WhenAny(drain, Task.Delay(timeout, ct)).ConfigureAwait(false);
            if (finished != drain)
            {
                _logger.Log(Bun3LogLevel.Warning, $"Server stop: {entries.Length} session(s) did not drain within {timeout}.");
            }

            _logger.Log(Bun3LogLevel.Info, "Server stopped.");
        }

        private void HandleConnected(IConnection connection)
        {
            TSession session;
            try
            {
                session = CreateSession(connection);
            }
            catch (Exception ex)
            {
                _logger.Log(Bun3LogLevel.Error, $"CreateSession failed for connection {connection.Id}; closing.", ex);
                connection.Close();
                return;
            }

            session.Initialize(_logger, _sessionOptions);
            var entry = new SessionEntry(session, session.RunAsync());
            _sessions[connection.Id] = entry;
        }

        private void HandleFrame(IConnection connection, ReadOnlyMemory<byte> frame)
        {
            if (_sessions.TryGetValue(connection.Id, out var entry))
            {
                entry.Session.EnqueueFrame(frame);
            }
        }

        private void HandleClosed(IConnection connection, Exception? error)
        {
            if (_sessions.TryRemove(connection.Id, out var entry))
            {
                entry.Session.NotifyClosed(error);
            }
        }

        private sealed class SessionEntry
        {
            public readonly TSession Session;
            public readonly Task RunTask;

            public SessionEntry(TSession session, Task runTask)
            {
                Session = session;
                RunTask = runTask;
            }
        }

        private sealed class Handler : IConnectionHandler
        {
            private readonly ServerBase<TSession> _server;

            public Handler(ServerBase<TSession> server) => _server = server;

            public void OnConnected(IConnection connection) => _server.HandleConnected(connection);

            public void OnFrame(IConnection connection, ReadOnlyMemory<byte> frame) =>
                _server.HandleFrame(connection, frame);

            public void OnClosed(IConnection connection, Exception? error) =>
                _server.HandleClosed(connection, error);
        }
    }
}
