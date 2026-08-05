using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Messaging.ControlMessages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Messaging
{
    /// <summary>
    /// 타입 있는 요청/응답과 푸시 구독을 제공하는 클라이언트.
    /// 서버 판정은 Reply 값으로, 인프라 실패(타임아웃·연결 종료)는 예외로 구분된다.
    /// </summary>
    public sealed class MessagingClient<TRequest, TResponse, TUpdate>
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        private sealed class Pending
        {
            public readonly TaskCompletionSource<(int Status, IMessage? Payload)> Tcs =
                new TaskCompletionSource<(int, IMessage?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly MessagingSchema<TRequest, TResponse, TUpdate> _schema;
        private readonly MessagingClientOptions _options;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<long, Pending> _pending =
            new ConcurrentDictionary<long, Pending>();
        private readonly ConcurrentDictionary<Type, Action<IMessage>> _updateHandlers =
            new ConcurrentDictionary<Type, Action<IMessage>>();
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

        private IConnection? _connection;
        private SynchronizationContext? _syncContext;
        private long _nextRequestId;
        private long _lastRttMs = -1;
        private volatile bool _closed;

        private MessagingClient(MessagingClientOptions options, ILogger logger)
        {
            _schema = MessagingSchema<TRequest, TResponse, TUpdate>.Create();
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
        public static async ValueTask<MessagingClient<TRequest, TResponse, TUpdate>> ConnectAsync(
            IConnector connector,
            MessagingClientOptions? options = null,
            ILogger? logger = null,
            CancellationToken ct = default)
        {
            if (connector == null)
            {
                throw new ArgumentNullException(nameof(connector));
            }

            var client = new MessagingClient<TRequest, TResponse, TUpdate>(
                options ?? new MessagingClientOptions(),
                new SafeLogger(logger ?? NullLogger.Instance));
            if (client._options.UseSynchronizationContext)
            {
                client._syncContext = SynchronizationContext.Current;
            }

            client._connection = await connector.ConnectAsync(new Handler(client), ct).ConfigureAwait(false);
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

            if (_closed)
            {
                throw new ConnectionClosedException("이미 종료된 연결");
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

            var pending = new Pending();
            _pending[requestId] = pending;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.RequestTimeout);
            using var registration = timeoutCts.Token.Register(() =>
            {
                if (_pending.TryRemove(requestId, out var removed))
                {
                    if (ct.IsCancellationRequested)
                    {
                        removed.Tcs.TrySetCanceled(ct);
                    }
                    else
                    {
                        removed.Tcs.TrySetException(
                            new TimeoutException($"요청 {requestId} 응답 없음 ({_options.RequestTimeout})"));
                    }
                }
            });

            await SendAsync(Channels.Request, envelope).ConfigureAwait(false);
            var (status, payload) = await pending.Tcs.Task.ConfigureAwait(false);

            if (status != 0)
            {
                return Reply<TRes>.Fail(status);
            }

            return payload is TRes typed
                ? Reply<TRes>.Ok(typed)
                : throw new InvalidOperationException(
                    $"응답 본문 타입 불일치: {payload?.GetType().Name ?? "없음"} (기대: {typeof(TRes).Name})");
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
            pending.Tcs.TrySetResult((status, payload));
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
                    pending.Tcs.TrySetException(new ConnectionClosedException("응답 대기 중 연결 종료"));
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
        }

        private ValueTask SendAsync(byte channel, IMessage message)
        {
            var connection = _connection;
            if (connection == null || _closed)
            {
                return default;
            }

            var body = message.ToByteArray();
            var packet = new byte[1 + body.Length];
            packet[0] = channel;
            body.CopyTo(packet, 1);
            return connection.SendAsync(packet);
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
            private readonly MessagingClient<TRequest, TResponse, TUpdate> _client;

            public Handler(MessagingClient<TRequest, TResponse, TUpdate> client) => _client = client;

            public void OnConnected(IConnection connection) => _client._connection = connection;

            public void OnPacket(IConnection connection, ReadOnlyMemory<byte> packet) =>
                _client.HandlePacket(packet);

            public void OnClosed(IConnection connection, Exception? error) => _client.HandleClosed(error);
        }
    }
}
