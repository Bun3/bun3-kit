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
    /// 연결 1개의 서버측 대응물(연결과 수명을 같이한다). 패킷은 세션별 큐에 쌓이고
    /// 단일 소비 루프가 순서대로 처리하므로, 한 세션의 핸들러는 절대 동시에 실행되지 않는다.
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

        /// <summary>주어진 연결에 바인딩된 세션을 생성한다.</summary>
        protected Session(IConnection connection)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>연결 식별자와 동일한 세션 식별자.</summary>
        public long Id => Connection.Id;

        /// <summary>이 세션이 바인딩된 연결.</summary>
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

        /// <summary>패킷 하나를 이 세션의 연결로 송신한다.</summary>
        public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default) =>
            Connection.SendAsync(packet, ct);

        /// <summary>서버 주도로 연결을 끊는다. 전송의 OnClosed 통지를 거쳐 세션이 정리된다.</summary>
        public void Kick() => Connection.Close();

        /// <summary>
        /// 세션 액터 큐에 작업을 주입한다 — 패킷 처리와 같은 줄에서 순차 실행되므로
        /// 핸들러와 같은 상태를 락 없이 만질 수 있다. 세션이 닫혔거나 큐가 상한이면
        /// false(작업 미실행). 작업의 미처리 예외는 로그만 남기고 세션은 유지된다.
        /// 종료 직전 경합 시 true를 반환하고도 실행되지 않을 수 있다(최선 노력).
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
                return false;   // 패킷 오버플로와 달리 킥하지 않는다 — 호출자가 스킵을 판단
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

        internal void EnqueuePacket(ReadOnlyMemory<byte> packet)
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

                    var dequeued = _inbox.TryDequeue(out var item);
                    System.Diagnostics.Debug.Assert(dequeued, "signal/inbox invariant broken");

                    Interlocked.Decrement(ref _queuedCount);
                    if (item is byte[] packet)
                    {
                        try
                        {
                            await WatchAsync(() => OnPacketAsync(packet), "handler").ConfigureAwait(false);
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
                            await WatchAsync(work, "posted work").ConfigureAwait(false);
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

        private async ValueTask WatchAsync(Func<ValueTask> action, string kind)
        {
            if (_slowWorkWarning <= TimeSpan.Zero)
            {
                await action().ConfigureAwait(false);
                return;
            }

            var pending = action();
            if (pending.IsCompleted)
            {
                await pending.ConfigureAwait(false);   // 동기 완료 — 감시 불필요 (fast path, 무할당)
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

            delayCts.Cancel();   // 타이머 즉시 정리 (고부하에서 타이머 적체 방지)
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
