using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Rpc.ControlMessages;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Rpc
{
    /// <summary>
    /// 메시징 계층의 세션 베이스. 원시 패킷 처리(OnPacketAsync)는 프레임워크가 소유하고,
    /// 게임은 OnSessionOpenedAsync/OnSessionClosedAsync 훅과 등록된 핸들러로만 참여한다.
    /// </summary>
    public abstract class RpcSession : Session
    {
        private IRpcRuntime? _runtime;
        private CancellationTokenSource? _watchdogCts;
        private long _lastReceivedTicksUtc;
        private int _disconnectSent;

        /// <summary>주어진 연결에 바인딩된 메시징 세션을 생성한다.</summary>
        protected RpcSession(IConnection connection) : base(connection) { }

        /// <summary>메시징 기본값: 핸들러 예외는 status=2 응답 + 세션 유지. 게임이 재정의 가능.</summary>
        protected override ErrorDecision OnHandlerError(Exception ex) => ErrorDecision.Continue;

        /// <summary>연결 수립 훅 (v0 OnConnectedAsync 대체). 여기서 예외가 나가면 세션은 무조건 킥된다(OnHandlerError 무관).</summary>
        protected virtual ValueTask OnSessionOpenedAsync() => default;

        /// <summary>세션 종료 훅 (v0 OnDisconnectedAsync 대체). 정상 종료면 error는 null.</summary>
        protected virtual ValueTask OnSessionClosedAsync(Exception? error) => default;

        /// <summary>
        /// 요청 디스패치 직전 호출되는 게이트. RpcStatus.Ok(0)이면 진행,
        /// 비0이면 그 상태코드로 즉시 응답하고 핸들러에 도달하지 않는다.
        /// Control 채널(Ping)은 게이트 대상이 아니다.
        /// </summary>
        protected internal virtual int OnGateRequest(Type requestType) => RpcStatus.Ok;

        /// <summary>서버 푸시. update는 게임 Update oneof의 케이스 타입이어야 한다.</summary>
        public ValueTask SendUpdateAsync(IMessage update) =>
            RequireRuntime().SendUpdateAsync(this, update);

        /// <summary>사유 코드와 함께 킥한다 — Disconnect{code}를 best-effort 송신(1초 상한,
        /// 예외 무시, 최초 1회만) 후 연결을 닫는다. 멱등.</summary>
        public override void Kick(int reasonCode)
        {
            if (reasonCode == DisconnectCode.None)
            {
                Kick();   // 0은 와이어에 싣지 않는다 — 사유 없는 킥과 동일 처리 (원샷 가드도 소모 안 함)
                return;
            }

            if (Interlocked.Exchange(ref _disconnectSent, 1) != 0)
            {
                // 사유는 이미 송신 중 — 그 작업의 finally가 닫기를 보장한다(최대 1초 상한).
                // 여기서 raw Kick()으로 즉시 닫으면 아직 플러시 중인 송신을 끊어버릴 수 있다.
                return;
            }

            _ = SendDisconnectThenCloseAsync(reasonCode);
        }

        private async Task SendDisconnectThenCloseAsync(int reasonCode)
        {
            try
            {
                var control = new Control { Disconnect = new Disconnect { Code = reasonCode } };
                var send = SendAsync(PacketWriter.Wrap(Channels.Control, control)).AsTask();
                await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
            }
            catch
            {
                // best-effort — 어차피 끊는 중
            }
            finally
            {
                Kick();
            }
        }

        /// <summary>v0 연결 훅. 메시징 계층이 소유하므로 봉인되어 있다 — 게임은 OnSessionOpenedAsync를 재정의한다.</summary>
        protected sealed override async ValueTask OnConnectedAsync()
        {
            Volatile.Write(ref _lastReceivedTicksUtc, DateTime.UtcNow.Ticks);
            StartIdleWatchdog();
            try
            {
                await OnSessionOpenedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // open 훅 실패 = 반초기화 상태 — 에러 정책 역전(요청 핸들러 한정)과 무관하게 항상 킥
                RequireRuntime().Logger.LogError(ex, "Session {SessionId}: OnSessionOpenedAsync threw; kicking.", Id);
                Kick();
            }
        }

        /// <summary>v0 패킷 훅. 메시징 계층이 소유하므로 봉인되어 있다 — 원시 패킷은 항상 런타임이 처리한다.</summary>
        protected sealed override ValueTask OnPacketAsync(ReadOnlyMemory<byte> packet)
        {
            Volatile.Write(ref _lastReceivedTicksUtc, DateTime.UtcNow.Ticks);
            return RequireRuntime().ProcessPacketAsync(this, packet);
        }

        /// <summary>v0 연결 종료 훅. 메시징 계층이 소유하므로 봉인되어 있다 — 게임은 OnSessionClosedAsync를 재정의한다.</summary>
        protected sealed override ValueTask OnDisconnectedAsync(Exception? error)
        {
            _watchdogCts?.Cancel();
            return OnSessionClosedAsync(error);
        }

        internal void AttachRuntime(IRpcRuntime runtime) => _runtime = runtime;

        internal ErrorDecision RaiseHandlerError(Exception ex) => OnHandlerError(ex);

        private IRpcRuntime RequireRuntime() =>
            _runtime ?? throw new InvalidOperationException(
                "런타임 미부착 — RpcSession은 RpcServer를 통해서만 생성되어야 한다.");

        private void StartIdleWatchdog()
        {
            var timeout = RequireRuntime().IdleKickTimeout;
            if (timeout == null)
            {
                return;
            }

            _watchdogCts = new CancellationTokenSource();
            _ = RunWatchdogAsync(timeout.Value, _watchdogCts.Token);
        }

        private async Task RunWatchdogAsync(TimeSpan timeout, CancellationToken ct)
        {
            var interval = TimeSpan.FromTicks(Math.Max(timeout.Ticks / 2, TimeSpan.TicksPerMillisecond * 50));
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    var last = new DateTime(Volatile.Read(ref _lastReceivedTicksUtc), DateTimeKind.Utc);
                    if (DateTime.UtcNow - last > timeout)
                    {
                        RequireRuntime().Logger.LogInformation(
                            "Session {SessionId}: idle for {Timeout}; kicking.", Id, timeout);
                        Kick(DisconnectCode.IdleKick);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 세션 종료로 인한 정상 취소
            }
        }
    }
}
