using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Rpc;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Players
{
    /// <summary>
    /// accountKey당 1개, 재접속에 살아남는 단위. 상태(재화·인벤토리 등)는 이 파생
    /// 클래스에 둔다. 훅들은 레지스트리의 계정 키 스트라이프 락 안에서 실행되므로
    /// 훅 안에서 SignInAsync/Kick을 재호출하면 안 된다(교착).
    /// 중복 로그인(NewWins) 이전 시 옛 세션은 즉시 무권한화되어(Player=null) 큐에 남은
    /// 요청이 게이트에서 차단된다 — 단, 이전 순간에 이미 실행 중이던 핸들러 1건은
    /// 선점되지 않으므로 PlayerTicker가 틱/저장 실행 직전에 소유권을 재확인한다. 저장 지점은
    /// 셋: 주기 스윕·detach(둘 다 dirty일 때만)와 은퇴(OnRetiredAsync, dirty 무관).
    /// </summary>
    public abstract class Player
    {
        /// <summary>불투명 신원 키 (권장 규약 "provider:subject"). SignIn 시 설정된다.</summary>
        public string AccountKey { get; internal set; } = "";

        /// <summary>접속 중이면 현재 세션, 유예 중이면 null.</summary>
        public RpcSession? CurrentSession { get; internal set; }

        /// <summary>현재 세션에 접속 중인지 여부.</summary>
        public bool IsConnected => CurrentSession != null;

        /// <summary>세션 바인딩 직후. isReconnect=true면 유예 재바인딩 또는 중복 로그인 이전.</summary>
        protected internal virtual ValueTask OnAttachedAsync(bool isReconnect) => default;

        /// <summary>연결 끊김(유예 시작) 시.</summary>
        protected internal virtual ValueTask OnDetachedAsync() => default;

        /// <summary>유예 만료·RetireAll 시 — 저장 지점. 이후 레지스트리에서 제거된다.</summary>
        protected internal virtual ValueTask OnRetiredAsync() => default;

        /// <summary>접속 중이면 현재 세션으로 푸시하고 true, 유예 중이면 false.</summary>
        public async ValueTask<bool> PushUpdateAsync(IMessage update)
        {
            var session = CurrentSession;
            if (session == null)
            {
                return false;
            }

            await session.SendUpdateAsync(update).ConfigureAwait(false);
            return true;
        }

        internal long LastTickAtTicksUtc;    // PlayerTicker 전용 — Attach 시 리셋
        internal long NextSaveAtTicksUtc;    // PlayerTicker 전용 — Attach 시 재무장

        // PlayerTicker 전용 틱 작업 캐시 — 세션 재바인딩 시에만 재생성해 틱당 클로저 할당을 없앤다.
        // 틱 루프 스레드에서만 읽고 쓴다.
        internal Func<ValueTask>? TickWork;
        internal object? TickWorkSession;

        /// <summary>접속 중일 때 주기 호출되는 틱 훅 — 현재 세션 액터 안에서 실행되므로
        /// 요청 핸들러와 동시에 실행되지 않는다. delta는 지난 틱 이후 실제 경과
        /// (재바인딩 시 리셋 — 오프라인 구간은 OnAttachedAsync에서 게임이 처리).
        /// 제약은 핸들러와 동일: 짧게, 자기/타 세션 완료를 동기 대기하지 말 것.</summary>
        protected internal virtual ValueTask OnTickAsync(TimeSpan delta) => default;

        /// <summary>저장 훅 — 게임이 DB 쓰기를 구현한다. 주기 스윕과 연결 끊김(detach)
        /// 시 — 둘 다 dirty일 때만 — 호출된다. 유예 만료의 최종 지점은 OnRetiredAsync.</summary>
        protected internal virtual ValueTask OnSaveAsync() => default;

        private int _dirtyVersion;
        private int _savedVersion;

        /// <summary>상태 변경 후 호출 — 다음 저장 주기의 대상으로 표시한다.
        /// 저장이 진행 중일 때 호출해도 그 변경은 다음 저장 대상으로 살아남는다(버전 카운터).</summary>
        public void MarkDirty() => Interlocked.Increment(ref _dirtyVersion);

        /// <summary>저장 대기 중인 변경이 있는지 여부.</summary>
        public bool IsDirty => Volatile.Read(ref _dirtyVersion) != Volatile.Read(ref _savedVersion);

        internal async ValueTask TrySaveAsync(ILogger logger)
        {
            var capturedVersion = Volatile.Read(ref _dirtyVersion);
            try
            {
                await OnSaveAsync().ConfigureAwait(false);
                Volatile.Write(ref _savedVersion, capturedVersion);   // 저장 중 MarkDirty는 버전이 앞서 dirty 유지
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OnSaveAsync 실패 — dirty 유지, 다음 주기에 재시도 (Player {AccountKey})", AccountKey);
            }
        }
    }
}
