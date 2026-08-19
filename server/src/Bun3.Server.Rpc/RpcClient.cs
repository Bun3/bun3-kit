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
    /// Client providing typed request/response and push subscriptions.
    /// Server verdicts arrive as Reply values; infrastructure failures (timeout, connection closed) as exceptions.
    /// </summary>
    public sealed class RpcClient<TRequest, TResponse, TUpdate> : IDisposable
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
        private int _receivedDisconnectCode;
        private int _disposed;

        private RpcClient(RpcClientOptions options, ILogger logger)
        {
            _schema = RpcSchema<TRequest, TResponse, TUpdate>.Create();
            _options = options;
            _logger = logger;
        }

        /// <summary>Round-trip time of the last Ping in ms; -1 before first measurement.</summary>
        public long LastRttMs => Volatile.Read(ref _lastRttMs);

        /// <summary>Whether the connection is open and not yet closed.</summary>
        public bool IsConnected => !_closed && _connection?.IsOpen == true;

        /// <summary>Raised once when the connection closes. Code 0 = no reason received (voluntary Close / network).
        /// Invoked on the captured context when UseSynchronizationContext is set.</summary>
        public event Action<DisconnectInfo>? Closed;

        /// <summary>Establishes a connection through the connector and creates the client. Captures the SynchronizationContext at connect time.</summary>
        /// <param name="connector">Connector that establishes the actual socket connection.</param>
        /// <param name="options">Client options; defaults when null.</param>
        /// <param name="logger">Logger; no-op logger when null.</param>
        /// <param name="configure">Setup applied to the client before the socket opens (mainly OnUpdate subscriptions) — prevents losing server pushes sent right after connect.</param>
        /// <param name="ct">Token to cancel connection establishment.</param>
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

            // Handler.OnConnected already assigns client._connection; only completion needs awaiting here.
            _ = await connector.ConnectAsync(new Handler(client), ct).ConfigureAwait(false);
            client.StartPingLoop();
            return client;
        }

        /// <summary>Sends a request and awaits the response. Server verdicts come as Reply; infrastructure failures as exceptions.</summary>
        public async ValueTask<Reply<TRes>> RequestAsync<TRes>(IMessage request, CancellationToken ct = default)
            where TRes : class, IMessage<TRes>
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var requestCase = _schema.RequestMap.ByPayloadType(request.GetType())
                ?? throw new ArgumentException($"Type not in Request oneof: {request.GetType().Name}", nameof(request));
            var responseCase = _schema.ResponseMap.ByFieldNumber(requestCase.FieldNumber);
            if (responseCase != null && responseCase.PayloadType != typeof(TRes))
            {
                throw new ArgumentException(
                    $"Response type of {requestCase.Name} is {responseCase.PayloadType.Name} — TRes mismatch", nameof(TRes));
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
                    // Re-check: after insert HandleClosed can see the entry — cancel first so a racing
                    // HandleClosed.TrySetException does not leave an UnobservedTaskException on an unobserved TCS.
                    pending.TrySetCanceled();
                    throw new ConnectionClosedException("Connection already closed");
                }

                using var timeoutCts = ct.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                    : new CancellationTokenSource();   // common default-ct path — skip linked-source/registration allocation
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
                                new TimeoutException($"No response for request {requestId} ({_options.RequestTimeout})"));
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
                        $"Response body type mismatch: {payload?.GetType().Name ?? "none"} (expected: {typeof(TRes).Name})");
            }
            finally
            {
                _pending.TryRemove(requestId, out _);   // reclaim the entry on every exit path
            }
        }

        /// <summary>Subscribes to pushes. Re-registering the same type replaces the handler. Unregistered updates are logged and ignored.</summary>
        public void OnUpdate<TUpd>(Action<TUpd> handler) where TUpd : class, IMessage<TUpd>
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _updateHandlers[typeof(TUpd)] = message => handler((TUpd)message);
        }

        /// <summary>Closes the connection. Pending requests fail with ConnectionClosedException.</summary>
        public void Close() => _connection?.Close();

        /// <summary>Closes the connection and cleans up internals (ping-loop CTS). Idempotent.
        /// The Closed event may fire after Dispose returns — do not touch disposed objects in the callback.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Close();
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
        }

        private void HandlePacket(byte[] packet)
        {
            if (packet.Length < 1)
            {
                ViolationClose("Empty packet");
                return;
            }

            var channel = packet[0];
            switch (channel)
            {
                case Channels.Response:
                    HandleResponse(packet);
                    break;
                case Channels.Update:
                    HandleUpdate(packet);
                    break;
                case Channels.Control:
                    HandleControl(packet);
                    break;
                default:
                    ViolationClose($"Disallowed channel 0x{channel:X2}");
                    break;
            }
        }

        private void HandleResponse(byte[] packet)
        {
            TResponse envelope;
            try
            {
                envelope = _schema.ResponseParser.ParseFrom(packet, 1, packet.Length - 1);   // zero-copy parse
            }
            catch (InvalidProtocolBufferException ex)
            {
                ViolationClose($"Response parse failure: {ex.Message}");
                return;
            }

            var requestId = (long)_schema.RequestIdOfResponse.Accessor.GetValue(envelope);
            if (!_pending.TryRemove(requestId, out var pending))
            {
                _logger.LogWarning("Response with no matching request_id={RequestId} — ignored", requestId);
                return;
            }

            var status = (int)_schema.StatusOfResponse.Accessor.GetValue(envelope);
            var payload = status == 0 ? _schema.ResponseMap.GetActiveCase(envelope)?.Get(envelope) : null;
            pending.TrySetResult((status, payload));
        }

        private void HandleUpdate(byte[] packet)
        {
            TUpdate envelope;
            try
            {
                envelope = _schema.UpdateParser.ParseFrom(packet, 1, packet.Length - 1);   // zero-copy parse
            }
            catch (InvalidProtocolBufferException ex)
            {
                ViolationClose($"Update parse failure: {ex.Message}");
                return;
            }

            var updateCase = _schema.UpdateMap.GetActiveCase(envelope);
            if (updateCase == null)
            {
                _logger.LogWarning("Update without body — ignored");
                return;
            }

            if (!_updateHandlers.TryGetValue(updateCase.PayloadType, out var handler))
            {
                _logger.LogWarning("Unregistered Update {Case} — ignored", updateCase.Name);
                return;
            }

            var payload = updateCase.Get(envelope)!;
            DispatchUpdate(handler, payload);
        }

        // Update hot-path dispatch — allocates one state object instead of two closures (zero without a context).
        private void DispatchUpdate(Action<IMessage> handler, IMessage payload)
        {
            var context = _syncContext;
            if (context == null)
            {
                RunUpdate(handler, payload);
                return;
            }

            context.Post(UpdateCallback, new UpdateDispatch(this, handler, payload));
        }

        private static readonly SendOrPostCallback UpdateCallback = state =>
        {
            var dispatch = (UpdateDispatch)state!;
            dispatch.Client.RunUpdate(dispatch.Handler, dispatch.Payload);
        };

        private void RunUpdate(Action<IMessage> handler, IMessage payload)
        {
            try
            {
                handler(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Push/event callback exception");
            }
        }

        private sealed class UpdateDispatch
        {
            public readonly RpcClient<TRequest, TResponse, TUpdate> Client;
            public readonly Action<IMessage> Handler;
            public readonly IMessage Payload;

            public UpdateDispatch(RpcClient<TRequest, TResponse, TUpdate> client, Action<IMessage> handler, IMessage payload)
            {
                Client = client;
                Handler = handler;
                Payload = payload;
            }
        }

        private void HandleControl(byte[] packet)
        {
            Control control;
            try
            {
                control = Control.Parser.ParseFrom(packet, 1, packet.Length - 1);   // zero-copy parse
            }
            catch (InvalidProtocolBufferException ex)
            {
                ViolationClose($"Control parse failure: {ex.Message}");
                return;
            }

            if (control.BodyCase == Control.BodyOneofCase.Pong)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Volatile.Write(ref _lastRttMs, Math.Max(0, now - control.Pong.ClientTimeUnixMs));
            }
            else if (control.BodyCase == Control.BodyOneofCase.Disconnect)
            {
                Volatile.Write(ref _receivedDisconnectCode, control.Disconnect.Code);   // used in the disconnect notification
            }
            else
            {
                // Deliberately lenient: forward compatibility with new Control messages from future servers (server side is strict).
                _logger.LogWarning("Unexpected Control {Case} — ignored", control.BodyCase);
            }
        }

        private void HandleClosed(Exception? error)
        {
            _closed = true;
            try
            {
                _lifetimeCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Dispose() already cancelled and cleaned up — harmless (best-effort under a race).
            }

            foreach (var pair in _pending)
            {
                if (_pending.TryRemove(pair.Key, out var pending))
                {
                    pending.TrySetException(new ConnectionClosedException("Connection closed while awaiting response"));
                }
            }

            Dispatch(() => Closed?.Invoke(new DisconnectInfo(Volatile.Read(ref _receivedDisconnectCode), error)));
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
                // normal cancellation from connection close
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ping loop exception — measurement stopped");
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
                    _logger.LogError(ex, "Push/event callback exception");
                }
            }
        }

        private void ViolationClose(string reason)
        {
            _logger.LogWarning("Protocol violation — {Reason}; closing connection", reason);
            _connection?.Close();
        }

        private sealed class Handler : IConnectionHandler
        {
            private readonly RpcClient<TRequest, TResponse, TUpdate> _client;

            public Handler(RpcClient<TRequest, TResponse, TUpdate> client) => _client = client;

            public void OnConnected(IConnection connection) => _client._connection = connection;

            public void OnPacket(IConnection connection, byte[] packet) =>
                _client.HandlePacket(packet);

            public void OnClosed(IConnection connection, Exception? error) => _client.HandleClosed(error);
        }
    }
}
