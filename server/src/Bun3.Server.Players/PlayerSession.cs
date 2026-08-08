using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;

namespace Bun3.Server.Players
{
    /// <summary>
    /// Player 수명주기가 붙은 세션 베이스. 반드시 PlayerRegistry.Wrap을 거친 팩토리로
    /// 생성해야 한다(레지스트리·허용 목록 부착).
    /// </summary>
    public abstract class PlayerSession<TPlayer> : RpcSession where TPlayer : Player
    {
        private PlayerRegistry<TPlayer>? _registry;
        private HashSet<Type>? _unauthenticatedTypes;
        private int _signingIn;

        /// <summary>주어진 연결에 바인딩된 세션을 생성한다.</summary>
        protected PlayerSession(IConnection connection) : base(connection) { }

        /// <summary>인증 후 non-null. 미인증 요청은 게이트가 차단하므로 핸들러에선 null 아님.</summary>
        public TPlayer? Player { get; private set; }

        /// <summary>이 세션이 Player에 바인딩되었는지 여부.</summary>
        public bool IsAuthenticated => Player != null;

        /// <summary>
        /// 자격증명 검증(게임 몫) 후 호출하는 프레임워크 진입점. 신규 로드/유예 재바인딩/
        /// 중복 로그인 이전을 처리한다. 같은 세션의 동시·이중 호출은 원자적으로 거부되어
        /// InvalidOperationException, RejectNew 정책에서 이미 접속 중이면 DuplicateLoginException.
        /// 실패(예외) 시 가드가 풀려 재시도할 수 있다.
        /// </summary>
        public async ValueTask<SignInResult<TPlayer>> SignInAsync(string accountKey)
        {
            if (Interlocked.CompareExchange(ref _signingIn, 1, 0) != 0)
            {
                throw new InvalidOperationException("이미 인증되었거나 SignInAsync가 진행 중인 세션이다.");
            }

            try
            {
                return await RequireRegistry().SignInAsync(this, accountKey).ConfigureAwait(false);
            }
            catch
            {
                Interlocked.Exchange(ref _signingIn, 0);
                throw;
            }
        }

        /// <summary>세션 종료 훅 (detach 처리 후 호출됨).
        /// 주의: 중복 로그인(NewWins)으로 킥된 세션에서는 Player 소유권이 이미 새 세션으로
        /// 이전된 뒤 실행될 수 있다 — 이 훅에서 Player를 저장하면 새 세션의 진행분을 덮어쓴다.
        /// 저장은 Player.OnRetiredAsync에서만 할 것.</summary>
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

        private PlayerRegistry<TPlayer> RequireRegistry() =>
            _registry ?? throw new InvalidOperationException(
                "레지스트리 미부착 — PlayerSession은 PlayerRegistry.Wrap을 거친 팩토리로 생성해야 한다.");
    }
}
