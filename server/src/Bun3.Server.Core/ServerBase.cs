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
        private readonly IServerLogger _logger;
        private readonly SessionOptions _sessionOptions;
        private readonly ConcurrentDictionary<long, SessionEntry> _sessions =
            new ConcurrentDictionary<long, SessionEntry>();
        private readonly Handler _handler;
        private volatile bool _running;

        protected ServerBase(
            ITransportListener transport,
            IServerLogger? logger = null,
            SessionOptions? sessionOptions = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = new SafeServerLogger(logger ?? NullServerLogger.Instance);
            _sessionOptions = sessionOptions ?? new SessionOptions();
            _handler = new Handler(this);
        }

        public bool IsRunning => _running;

        public IReadOnlyCollection<TSession> Sessions =>
            _sessions.Values.Select(e => e.Session).ToArray();

        protected abstract TSession CreateSession(IConnection connection);

        /// <remarks>단일 사용: StopAsync 이후 재시작할 수 없다. 새 인스턴스를 생성할 것.</remarks>
        public async Task StartAsync(CancellationToken ct = default)
        {
            await _transport.StartAsync(_handler, ct).ConfigureAwait(false);
            _running = true;
            _logger.Log(ServerLogLevel.Info, "Server started.");
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

            var drain = Task.WhenAll(entries.Select(e => e.Completion));
            var timeout = drainTimeout ?? DefaultDrainTimeout;
            var finished = await Task.WhenAny(drain, Task.Delay(timeout, ct)).ConfigureAwait(false);
            if (finished != drain)
            {
                _logger.Log(ServerLogLevel.Warning, $"Server stop: {entries.Length} session(s) did not drain within {timeout}.");
            }

            _logger.Log(ServerLogLevel.Info, "Server stopped.");
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
                _logger.Log(ServerLogLevel.Error, $"CreateSession failed for connection {connection.Id}; closing.", ex);
                connection.Close();
                return;
            }

            session.Initialize(_logger, _sessionOptions);
            var entry = new SessionEntry(session);
            _sessions[connection.Id] = entry;
            entry.BindRunTask(session.RunAsync());
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
            private readonly TaskCompletionSource<bool> _completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public SessionEntry(TSession session)
            {
                Session = session;
            }

            /// <summary>실제 RunAsync 태스크가 끝날 때 완료된다. 바인딩 전에는 완료되지 않으므로
            /// StopAsync가 등록~바인딩 사이의 엔트리를 관찰해도 드레인을 건너뛰지 않는다.</summary>
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

            public void OnFrame(IConnection connection, ReadOnlyMemory<byte> frame) =>
                _server.HandleFrame(connection, frame);

            public void OnClosed(IConnection connection, Exception? error) =>
                _server.HandleClosed(connection, error);
        }
    }
}
