using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;

namespace Bun3.Server.Players
{
    /// <summary>
    /// 현재/직전 디스패치(핸들러 호출)의 진행 상태를 추적하는 비제네릭 창구.
    /// PlayersConfig의 핸들러 래핑과 PlayerRegistry의 중복 로그인 킥이 TSession/TPlayer
    /// 제네릭 인자 없이도 세션의 진행 상태를 관찰하기 위해 쓴다.
    /// 목적: 같은 세션이 방금 자신의 SignInAsync 호출(반드시 그 세션의 "첫" 디스패치 —
    /// 재호출은 InvalidOperationException)로 새 계정을 만든 직후, 그 응답을 채 보내기도
    /// 전에 다른 세션의 중복 로그인이 이 세션을 킥해 응답이 유실되는 경합을 피한다.
    /// DispatchCount가 1보다 크면 그 첫 디스패치의 응답은 Session의 순차 처리 보장상
    /// (다음 패킷은 이전 패킷의 OnPacketAsync가 SendAsync까지 전부 끝나야 디큐된다)
    /// 이미 전송이 끝난 것이 결정적으로 보장되므로 그 경우엔 대기가 전혀 필요 없다.
    /// </summary>
    internal interface IDispatchSettlement
    {
        void MarkDispatchStart();
        void MarkDispatchEnd();
        Task? PendingDispatchTask { get; }

        /// <summary>이 세션이 지금까지 시작한 디스패치 총수(현재 것 포함).</summary>
        int DispatchCount { get; }
    }

    /// <summary>
    /// Player 수명주기가 붙은 세션 베이스. 반드시 PlayerRegistry.Wrap을 거친 팩토리로
    /// 생성해야 한다(레지스트리·허용 목록 부착).
    /// </summary>
    public abstract class PlayerSession<TPlayer> : RpcSession, IDispatchSettlement where TPlayer : Player
    {
        private PlayerRegistry<TPlayer>? _registry;
        private HashSet<Type>? _unauthenticatedTypes;
        private volatile TaskCompletionSource<bool>? _dispatchSettlement;
        private int _dispatchCount;

        /// <summary>주어진 연결에 바인딩된 세션을 생성한다.</summary>
        protected PlayerSession(IConnection connection) : base(connection) { }

        /// <summary>인증 후 non-null. 미인증 요청은 게이트가 차단하므로 핸들러에선 null 아님.</summary>
        public TPlayer? Player { get; private set; }

        /// <summary>이 세션이 Player에 바인딩되었는지 여부.</summary>
        public bool IsAuthenticated => Player != null;

        /// <summary>
        /// 자격증명 검증(게임 몫) 후 호출하는 프레임워크 진입점. 신규 로드/유예 재바인딩/
        /// 중복 로그인 이전을 처리한다. RejectNew 정책에서 이미 접속 중이면
        /// DuplicateLoginException, 같은 세션 이중 호출이면 InvalidOperationException.
        /// </summary>
        public ValueTask<SignInResult<TPlayer>> SignInAsync(string accountKey) =>
            RequireRegistry().SignInAsync(this, accountKey);

        /// <summary>세션 종료 훅 재노출 (detach 처리 후 호출됨).</summary>
        protected virtual ValueTask OnPlayerSessionClosedAsync(Exception? error) => default;

        /// <summary>인증 여부/허용 목록으로 요청을 게이트한다. Players 계층이 소유하므로 봉인.</summary>
        protected sealed override int OnGateRequest(Type requestType) =>
            Player != null || (_unauthenticatedTypes != null && _unauthenticatedTypes.Contains(requestType))
                ? RpcStatus.Ok
                : RpcStatus.Unauthenticated;

        /// <summary>세션 종료 처리 — detach를 레지스트리에 위임한 뒤 게임 훅을 호출한다. 봉인.</summary>
        protected sealed override async ValueTask OnSessionClosedAsync(Exception? error)
        {
            var registry = _registry;
            if (registry != null)
            {
                await registry.HandleSessionClosedAsync(this).ConfigureAwait(false);
            }

            await OnPlayerSessionClosedAsync(error).ConfigureAwait(false);
        }

        internal void AttachPlayers(PlayerRegistry<TPlayer> registry, HashSet<Type> unauthenticatedTypes)
        {
            _registry = registry;
            _unauthenticatedTypes = unauthenticatedTypes;
        }

        internal void SetPlayer(TPlayer? player) => Player = player;

        void IDispatchSettlement.MarkDispatchStart()
        {
            Interlocked.Increment(ref _dispatchCount);
            _dispatchSettlement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        void IDispatchSettlement.MarkDispatchEnd() => _dispatchSettlement?.TrySetResult(true);

        Task? IDispatchSettlement.PendingDispatchTask => _dispatchSettlement?.Task;

        int IDispatchSettlement.DispatchCount => Volatile.Read(ref _dispatchCount);

        private PlayerRegistry<TPlayer> RequireRegistry() =>
            _registry ?? throw new InvalidOperationException(
                "레지스트리 미부착 — PlayerSession은 PlayerRegistry.Wrap을 거친 팩토리로 생성해야 한다.");
    }
}
