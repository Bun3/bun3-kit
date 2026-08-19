// Popup partial — 닫기 잠금(PopupCloseScope) 담당.
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    // 닫기 잠금: ref-count + 세대 토큰. 잠금 중 닫기 요청은 예약되고 마지막 해제 시 실행된다.
    public abstract partial class Popup
    {
        private int _closeScopeCount;
        private int _closeScopeVersion;

        /// <summary>
        /// 닫기 잠금이 하나라도 걸려 있는지. 잠금 중에는 <see cref="Close"/>/back에 의한 닫기가
        /// 거부되는 게 아니라 <b>예약</b>되며, 마지막 잠금이 풀릴 때 자동으로 실행된다.
        /// </summary>
        public bool IsCloseBlocked => _closeScopeCount > 0;

        /// <summary>
        /// 닫기 잠금을 건다(ref-count, 중첩 가능). 초기 데이터 로딩, 서버 응답 대기,
        /// 시퀀스 연출 등 "이 동안은 닫히면 안 되는" 구간을 <c>using</c>으로 감싼다.
        /// </summary>
        /// <example><code>
        /// using (BlockClose())
        ///     await PlaySequenceAsync(ct);
        /// </code></example>
        public PopupCloseScope BlockClose()
            => new(this, AcquireCloseScope());

        /// <summary>태스크가 끝날 때까지 닫기 잠금을 유지한다. 예외가 나도 잠금은 해제된다.</summary>
        public async UniTask BlockCloseWhile(UniTask task)
        {
            var version = AcquireCloseScope();
            try
            {
                await task;
            }
            finally
            {
                ReleaseCloseScope(version);
            }
        }

        /// <summary>
        /// 태스크가 끝날 때까지 닫기 잠금을 유지하고 결과를 돌려준다.
        /// 서버 요청-응답 패턴(<c>var res = await BlockCloseWhile(SendPacket(...))</c>)용.
        /// </summary>
        public async UniTask<T> BlockCloseWhile<T>(UniTask<T> task)
        {
            var version = AcquireCloseScope();
            try
            {
                return await task;
            }
            finally
            {
                ReleaseCloseScope(version);
            }
        }

        /// <summary>
        /// 닫기 잠금 상태가 바뀔 때(0→1 잠김, 1→0 풀림) 호출된다.
        /// 게임이 raycast 차단, 로딩 스피너 등 표현을 연결하는 지점. 기본은 아무것도 안 한다.
        /// </summary>
        protected virtual void OnCloseBlockedChanged(bool blocked) { }

        /// <returns>해제 시 대조할 세대 토큰 — Detach마다 증가해 이전 세션 스코프를 무효화한다.</returns>
        internal int AcquireCloseScope()
        {
            _closeScopeCount++;
            if (_closeScopeCount == 1)
                OnCloseBlockedChanged(true);

            return _closeScopeVersion;
        }

        internal void ReleaseCloseScope(int version)
        {
            // 이전 세션(Detach 이전)에 잡힌 가드의 늦은 해제가 새 세션 카운트를 훼손하지 않게.
            if (version != _closeScopeVersion || _closeScopeCount == 0)
                return;

            _closeScopeCount--;
            if (_closeScopeCount > 0)
                return;

            OnCloseBlockedChanged(false);

            if (CloseRequested && Phase == PopupPhase.Open)
                Stack?.Close(this);
        }
    }
}
