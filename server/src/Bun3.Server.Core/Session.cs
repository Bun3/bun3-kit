using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Core
{
    /// <summary>
    /// 연결 1개의 서버측 대응물(연결과 수명을 같이한다). 패킷은 세션별 큐에 쌓이고
    /// 단일 소비 루프가 순서대로 처리하므로, 한 세션의 핸들러는 절대 동시에 실행되지 않는다.
    /// </summary>
    public abstract class Session
    {
        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private IServerLogger _logger = NullServerLogger.Instance;
        private int _maxQueuedPackets = 256;
        private volatile bool _closed;
        private Exception? _closeError;
        private int _queuedCount;

        protected Session(IConnection connection)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public long Id => Connection.Id;

        public IConnection Connection { get; }

        /// <summary>연결이 수립되어 소비 루프가 시작될 때 1회 호출된다.</summary>
        protected virtual ValueTask OnConnectedAsync() => default;

        /// <summary>패킷 하나를 처리한다. 같은 세션에서 동시 실행되지 않는다.</summary>
        protected abstract ValueTask OnPacketAsync(ReadOnlyMemory<byte> packet);

        /// <summary>세션 종료 시 1회 호출된다. 정상 종료면 error는 null.</summary>
        protected virtual ValueTask OnDisconnectedAsync(Exception? error) => default;

        /// <summary>
        /// OnConnectedAsync/OnPacketAsync가 던진 예외의 처리 방침. 기본값은 세션 종료.
        /// "이 예외는 무시해도 안전하다"는 지식이 있는 게임만 재정의한다.
        /// </summary>
        protected virtual ErrorDecision OnHandlerError(Exception ex) => ErrorDecision.CloseSession;

        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default) =>
            Connection.SendAsync(packet, ct);

        /// <summary>서버 주도로 연결을 끊는다. 전송의 OnClosed 통지를 거쳐 세션이 정리된다.</summary>
        public void Kick() => Connection.Close();

        internal void Initialize(IServerLogger logger, int maxQueuedPackets)
        {
            _logger = logger;
            _maxQueuedPackets = maxQueuedPackets;
        }

        internal void EnqueuePacket(ReadOnlyMemory<byte> packet)
        {
            if (_closed)
            {
                return;
            }

            if (Interlocked.Increment(ref _queuedCount) > _maxQueuedPackets)
            {
                Interlocked.Decrement(ref _queuedCount);
                _logger.Log(ServerLogLevel.Warning,
                    $"Session {Id}: inbox overflow (>{_maxQueuedPackets}); kicking.");
                Kick();
                return;
            }

            _inbox.Enqueue(packet.ToArray()); // 버퍼는 호출 동안만 유효하므로 복사
            _signal.Release();
        }

        internal void NotifyClosed(Exception? error)
        {
            _closeError = error;
            _closed = true;
            _signal.Release(); // 소비 루프를 깨워 종료시킨다
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
                        break; // 종료 후 잔여 패킷은 처리하지 않는다
                    }

                    var dequeued = _inbox.TryDequeue(out var packet);
                    System.Diagnostics.Debug.Assert(dequeued, "signal/inbox invariant broken");

                    Interlocked.Decrement(ref _queuedCount);
                    try
                    {
                        await OnPacketAsync(packet).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        HandleError(ex);
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
                    _logger.Log(ServerLogLevel.Error, $"Session {Id}: OnDisconnectedAsync threw.", ex);
                }
            }
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
                _logger.Log(ServerLogLevel.Error, $"Session {Id}: OnHandlerError threw.", hookEx);
                decision = ErrorDecision.CloseSession;
            }

            if (decision == ErrorDecision.CloseSession)
            {
                _logger.Log(ServerLogLevel.Error, $"Session {Id}: handler exception; closing session.", ex);
                Kick();
            }
            else
            {
                _logger.Log(ServerLogLevel.Warning, $"Session {Id}: handler exception ignored by OnHandlerError.", ex);
            }
        }
    }
}
