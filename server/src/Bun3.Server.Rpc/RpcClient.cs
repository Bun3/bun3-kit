using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc.ControlMessages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Rpc
{
    /// <summary>
    /// 타입 있는 요청/응답과 푸시 구독을 제공하는 클라이언트.
    /// 서버 판정은 Reply 값으로, 인프라 실패(타임아웃·연결 종료)는 예외로 구분된다.
    /// </summary>
    public sealed class RpcClient<TRequest, TResponse, TUpdate>
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        private readonly RpcSchema<TRequest, TResponse, TUpdate> _schema;
        private readonly RpcClientOptions _options;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<long, TaskCompletionSource<(int Status, IMessage? Payload)>> _pending =
            new ConcurrentDictionary<long, TaskCompletionSource<(int Status, IMessage? Payload)>>();
        private readonly ConcurrentDictionary<Type, Action<IMessage>> _updateHandlers =
            new ConcurrentDictionary<Type, Action<IMessage>>();
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

        private IConnection? _connection;
        private SynchronizationContext? _syncContext;
        private long _nextRequestId;
        private long _lastRttMs = -1;
        private volatile bool _closed;

        private RpcClient(RpcClientOptions options, ILogger logger)
        {
            _schema = RpcSchema<TRequest, TResponse, TUpdate>.Create();
            _options = options;
            _logger = logger;
        }

        /// <summary>마지막 Ping의 왕복 시간(ms). 측정 전에는 -1.</summary>
        public long LastRttMs => Volatile.Read(ref _lastRttMs);

        /// <summary>연결이 열려 있고 아직 닫히지 않았는지 여부.</summary>
        public bool IsConnected => !_closed && _connection?.IsOpen == true;

        /// <summary>연결 종료 시 1회. 정상 종료면 null. UseSynchronizationContext 시 캡처 컨텍스트에서 호출.</summary>
        public event Action<Exception?>? Closed;

        /// <summary>커넥터로 연결을 수립하고 클라이언트를 생성한다. 접속 시점의 SynchronizationContext를 캡처한다.</summary>
        /// <param name="connector">실제 소켓 연결을 수립하는 커넥터.</param>
        /// <param name="options">클라이언트 옵션. null이면 기본값.</param>
        /// <param name="logger">로거. null이면 무동작 로거.</param>
        /// <param name="configure">소켓이 열리기 전에 클라이언트에 적용할 설정(주로 OnUpdate 구독) — 접속 직후 서버 푸시의 유실을 막는다.</param>
        /// <param name="ct">연결 수립을 취소할 토큰.</param>
        public static async ValueTask<RpcClient<TRequest, TResponse, TUpdate>> ConnectAsync(
            IConnector connector,
            RpcClientOptions? options = null,
            ILogger? logger = null,
            Action<RpcClient<TRequest, TResponse, TUpdate>>? configure = null,
            CancellationToken ct = default)
        {
            if (connector == null)
            {
                throw new ArgumentNullException(nameof(connector));
            }

            var client = new RpcClient<TRequest, TResponse, TUpdate>(
                options ?? new RpcClientOptions(),
                new SafeLogger(logger ?? NullLogger.Instance));
            if (client._options.UseSynchronizationContext)
            {
                client._syncContext = SynchronizationContext.Current;
            }

            configure?.Invoke(client);

            // Handler.OnConnected가 이미 client._connection을 할당하므로, 여기선 완료를 기다리기만 하면 된다.
            _ = await connector.ConnectAsync(new Handler(client), ct).ConfigureAwait(false);
            client.StartPingLoop();
            return client;
        }

        /// <summary>요청을 보내고 응답을 기다린다. 서버 판정은 Reply로, 인프라 실패는 예외로 온다.</summary>
        public async ValueTask<Reply<TRes>> RequestAsync<TRes>(IMessage request, CancellationToken ct = default)
            where TRes : class, IMessage<TRes>
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var requestCase = _schema.RequestMap.ByPayloadType(request.GetType())
                ?? throw new ArgumentException($"Request oneof에 없는 타입: {request.GetType().Name}", nameof(request));
            var responseCase = _schema.ResponseMap.ByFieldNumber(requestCase.FieldNumber);
            if (responseCase != null && responseCase.PayloadType != typeof(TRes))
            {
                throw new ArgumentException(
                    $"{requestCase.Name}의 응답 타입은 {responseCase.PayloadType.Name} — TRes 불일치", nameof(TRes));
            }

            var requestId = Interlocked.Increment(ref _nextRequestId);
            var envelope = new TRequest();
            _schema.RequestIdOfRequest.Accessor.SetValue(envelope, requestId);
            requestCase.Set(envelope, request);

            var pending = new TaskCompletionSource<(int Status, IMessage? Payload)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[requestId] = pending;
            try
            {
                if (_closed)
                {
                    // 재확인: insert 후엔 HandleClosed가 보게 됨 — 경합하는 HandleClosed의 TrySetException이
                    // 아무도 관전하지 않는 TCS에 UnobservedTaskException을 남기지 않도록 먼저 취소로 관전 처리한다.
                    pending.TrySetCanceled();
                    throw new ConnectionClosedException("이미 종료된 연결");
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_options.RequestTimeout);
                using var registration = timeoutCts.Token.Register(() =>
                {
                    if (_pending.TryRemove(requestId, out var removed))
                    {
                        if (ct.IsCancellationRequested)
                        {
                            removed.TrySetCanceled(ct);
                        }
                        else
                        {
                            removed.TrySetException(
                                new TimeoutException($"요청 {requestId} 응답 없음 ({_options.RequestTimeout})"));
                        }
                    }
                });

                await SendAsync(Channels.Request, envelope).ConfigureAwait(false);
                var (status, payload) = await pending.Task.ConfigureAwait(false);

                if (status != 0)
                {
                    return Reply<TRes>.Fail(status);
                }

                return payload is TRes typed
                    ? Reply<TRes>.Ok(typed)
                    : throw new InvalidOperationException(
                        $"응답 본문 타입 불일치: {payload?.GetType().Name ?? "없음"} (기대: {typeof(TRes).Name})");
            }
            finally
            {
                _pending.TryRemove(requestId, out _);   // 어떤 경로로 끝나든 엔트리 회수
            }
        }

        /// <summary>푸시 구독. 같은 타입 재등록은 교체된다. 미등록 Update는 경고 로그 후 무시.</summary>
        public void OnUpdate<TUpd>(Action<TUpd> handler) where TUpd : class, IMessage<TUpd>
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _updateHandlers[typeof(TUpd)] = message => handler((TUpd)message);
        }

        /// <summary>연결을 닫는다. 대기 중인 요청은 ConnectionClosedException으로 실패한다.</summary>
        public void Close() => _connection?.Close();

        private void HandlePacket(ReadOnlyMemory<byte> packet)
        {
            if (packet.Length < 1)
            {
                ViolationClose("빈 패킷");
                return;
            }

            var channel = packet.Span[0];
            var body = packet.Slice(1).ToArray();
            switch (channel)
            {
                case Channels.Response:
                    HandleResponse(body);
                    break;
                case Channels.Update:
                    HandleUpdate(body);
                    break;
                case Channels.Control:
                    HandleControl(body);
                    break;
                default:
                    ViolationClose($"허용되지 않은 채널 0x{channel:X2}");
                    break;
            }
        }

        private void HandleResponse(byte[] body)
        {
            TResponse envelope;
            try
            {
                envelope = _schema.ResponseParser.ParseFrom(body);
            }
            catch (InvalidProtocolBufferException ex)
            {
                ViolationClose($"Response 파싱 실패: {ex.Message}");
                return;
            }

            var requestId = (long)_schema.RequestIdOfResponse.Accessor.GetValue(envelope);
            if (!_pending.TryRemove(requestId, out var pending))
            {
                _logger.LogWarning("대응 없는 응답 request_id={RequestId} — 무시", requestId);
                return;
            }

            var status = (int)_schema.StatusOfResponse.Accessor.GetValue(envelope);
            var payload = status == 0 ? _schema.ResponseMap.GetActiveCase(envelope)?.Get(envelope) : null;
            pending.TrySetResult((status, payload));
        }

        private void HandleUpdate(byte[] body)
        {
            TUpdate envelope;
            try
            {
                envelope = _schema.UpdateParser.ParseFrom(body);
            }
            catch (InvalidProtocolBufferException ex)
            {
                ViolationClose($"Update 파싱 실패: {ex.Message}");
                return;
            }

            var updateCase = _schema.UpdateMap.GetActiveCase(envelope);
            if (updateCase == null)
            {
                _logger.LogWarning("body 없는 Update — 무시");
                return;
            }

            if (!_updateHandlers.TryGetValue(updateCase.PayloadType, out var handler))
            {
                _logger.LogWarning("미등록 Update {Case} — 무시", updateCase.Name);
                return;
            }

            var payload = updateCase.Get(envelope)!;
            Dispatch(() => handler(payload));
        }

        private void HandleControl(byte[] body)
        {
            Control control;
            try
            {
                control = Control.Parser.ParseFrom(body);
            }
            catch (InvalidProtocolBufferException ex)
            {
                ViolationClose($"Control 파싱 실패: {ex.Message}");
                return;
            }

            if (control.BodyCase == Control.BodyOneofCase.Pong)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Volatile.Write(ref _lastRttMs, Math.Max(0, now - control.Pong.ClientTimeUnixMs));
            }
            else
            {
                // 의도적 관대함: 미래 서버의 새 Control 메시지와의 전방 호환 (서버 쪽은 엄격)
                _logger.LogWarning("예상 밖 Control {Case} — 무시", control.BodyCase);
            }
        }

        private void HandleClosed(Exception? error)
        {
            _closed = true;
            _lifetimeCts.Cancel();
            foreach (var pair in _pending)
            {
                if (_pending.TryRemove(pair.Key, out var pending))
                {
                    pending.TrySetException(new ConnectionClosedException("응답 대기 중 연결 종료"));
                }
            }

            Dispatch(() => Closed?.Invoke(error));
        }

        private void StartPingLoop()
        {
            var interval = _options.PingInterval;
            if (interval == null)
            {
                return;
            }

            _ = RunPingLoopAsync(interval.Value, _lifetimeCts.Token);
        }

        private async Task RunPingLoopAsync(TimeSpan interval, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    var ping = new Control
                    {
                        Ping = new Ping { ClientTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                    };
                    await SendAsync(Channels.Control, ping).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 연결 종료로 인한 정상 취소
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ping 루프 예외 — 측정 중단");
            }
        }

        private ValueTask SendAsync(byte channel, IMessage message)
        {
            var connection = _connection;
            if (connection == null || _closed)
            {
                return default;
            }

            return connection.SendAsync(PacketWriter.Wrap(channel, message));
        }

        private void Dispatch(Action action)
        {
            var context = _syncContext;
            if (context != null)
            {
                context.Post(_ => Run(action), null);
            }
            else
            {
                Run(action);
            }

            void Run(Action inner)
            {
                try
                {
                    inner();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "푸시/이벤트 콜백 예외");
                }
            }
        }

        private void ViolationClose(string reason)
        {
            _logger.LogWarning("프로토콜 위반 — {Reason}; 연결 종료", reason);
            _connection?.Close();
        }

        private sealed class Handler : IConnectionHandler
        {
            private readonly RpcClient<TRequest, TResponse, TUpdate> _client;

            public Handler(RpcClient<TRequest, TResponse, TUpdate> client) => _client = client;

            public void OnConnected(IConnection connection) => _client._connection = connection;

            public void OnPacket(IConnection connection, ReadOnlyMemory<byte> packet) =>
                _client.HandlePacket(packet);

            public void OnClosed(IConnection connection, Exception? error) => _client.HandleClosed(error);
        }
    }
}
