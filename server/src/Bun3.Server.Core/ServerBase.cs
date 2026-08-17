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
            // transport 기동 직후(플래그 세팅 전) 유입된 연결이 킥되지 않도록 먼저 올린다.
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
            using (var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var finished = await Task.WhenAny(drain, Task.Delay(timeout, delayCts.Token)).ConfigureAwait(false);
                if (finished == drain)
                {
                    delayCts.Cancel(); // 드레인 완료 — 잔여 타이머 즉시 정리
                    await drain.ConfigureAwait(false); // 결과 관찰(Completion은 폴트하지 않지만 규율상)
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
                // 전송 계약 위반(id 중복) — 기존 세션을 보존하고 신규 연결만 닫는다.
                // 세션은 RunAsync에 바인딩되지 않았으므로 수명주기 콜백 없이 폐기된다.
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
            else if (_logger.IsEnabled(LogLevel.Debug))   // 패킷 경로 — 레벨 꺼짐 시 인자 박싱 회피
            {
                // 정지/킥과의 경합에서도 유입될 수 있으므로 Debug 수준으로만 남긴다.
                _logger.LogDebug("Packet from unknown connection {ConnectionId}; dropped.", connection.Id);
            }
        }

        private void HandleClosed(IConnection connection, Exception? error)
        {
            // netstandard2.1에는 TryRemove(KeyValuePair)가 없어 ICollection.Remove로
            // 값 일치 조건부 제거를 원자적으로 수행한다.
            if (TryGetOwnedEntry(connection, out var entry)
                && ((ICollection<KeyValuePair<long, SessionEntry>>)_sessions).Remove(
                    new KeyValuePair<long, SessionEntry>(connection.Id, entry)))
            {
                entry.Session.NotifyClosed(error);
            }
        }

        /// <summary>이 연결 소유의 세션 엔트리만 찾는다 — 중복 id로 거부된 연결의 잔여
        /// 프레임/OnClosed가 같은 id의 원래 세션에 오폭되지 않게 참조 동일성으로 거른다.</summary>
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
