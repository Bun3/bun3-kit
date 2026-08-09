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
    /// 전송 리스너 위에서 연결→세션 바인딩과 수명주기를 관리하는 서버 베이스.
    /// 게임 코드와의 결합점은 CreateSession 팩토리 하나다.
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

        /// <summary>서버 베이스를 구성한다. transport는 시작 시 handler를 바인딩받는다.
        /// slowWorkWarning: 세션 큐 항목이 이 시간을 넘기면 경고 로그(null=1초, 0 이하=끔).</summary>
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

        /// <summary>StartAsync 이후 StopAsync 전까지 true.</summary>
        public bool IsRunning => _running;

        /// <summary>현재 연결되어 있는 세션들의 스냅샷.</summary>
        public IReadOnlyCollection<TSession> Sessions =>
            _sessions.Values.Select(e => e.Session).ToArray();

        /// <summary>새 연결에 대응하는 세션 인스턴스를 생성한다. 게임 코드와의 유일한 결합점.</summary>
        protected abstract TSession CreateSession(IConnection connection);

        /// <remarks>단일 사용: StopAsync 이후 재시작할 수 없다. 새 인스턴스를 생성할 것.</remarks>
        public async Task StartAsync(CancellationToken ct = default)
        {
            await _transport.StartAsync(_handler, ct).ConfigureAwait(false);
            _running = true;
            _logger.LogInformation("Server started.");
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
                entry.Session.Kick(DisconnectCode.ServerShutdown);
            }

            var drain = Task.WhenAll(entries.Select(e => e.Completion));
            var timeout = drainTimeout ?? DefaultDrainTimeout;
            var finished = await Task.WhenAny(drain, Task.Delay(timeout, ct)).ConfigureAwait(false);
            if (finished != drain)
            {
                _logger.LogWarning(
                    "Server stop: {SessionCount} session(s) did not drain within {Timeout}.", entries.Length, timeout);
            }

            _logger.LogInformation("Server stopped.");
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
                _logger.LogError(ex, "CreateSession failed for connection {ConnectionId}; closing.", connection.Id);
                connection.Close();
                return;
            }

            session.Initialize(_logger, _maxQueuedPackets, _slowWorkWarning);
            var entry = new SessionEntry(session);
            _sessions[connection.Id] = entry;
            entry.BindRunTask(session.RunAsync());
        }

        private void HandlePacket(IConnection connection, byte[] packet)
        {
            if (_sessions.TryGetValue(connection.Id, out var entry))
            {
                entry.Session.EnqueuePacket(packet);
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

            public void OnPacket(IConnection connection, byte[] packet) =>
                _server.HandlePacket(connection, packet);

            public void OnClosed(IConnection connection, Exception? error) =>
                _server.HandleClosed(connection, error);
        }
    }
}
