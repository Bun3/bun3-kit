using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 게임 팝업 프리팹의 베이스 컴포넌트. <see cref="PopupStack"/>이 생명주기를 소유하며,
    /// 게임은 열림/닫힘 연출과 back 키 반응만 가상 메서드로 구현한다.
    /// </summary>
    /// <remarks>
    /// <see cref="Key"/>/<see cref="Layer"/>/<see cref="Phase"/>는 스택이 설정한다 — 게임은 읽기만.
    /// 인스턴스 생성(<see cref="PopupFactory"/>)과 해제(<see cref="PopupReleaser"/>)는 게임 몫이므로,
    /// 이 클래스는 트랜스폼/캔버스 배치를 일절 건드리지 않는다.
    /// </remarks>
    public abstract class PopupBehaviour : MonoBehaviour
    {
        private UniTaskCompletionSource _closedSource;

        /// <summary>이 인스턴스를 연 키. 스택에 속해 있지 않으면 마지막 값이 남는다.</summary>
        public PopupKey Key { get; private set; }

        /// <summary>정렬 레이어. 높을수록 위. 같은 레이어 안에서는 나중 push가 위.</summary>
        public int Layer { get; private set; }

        /// <summary>현재 생명주기 단계.</summary>
        public PopupPhase Phase { get; private set; }

        /// <summary>이 팝업을 소유한 스택. 스택에 속해 있지 않으면 null.</summary>
        public PopupStack Stack { get; private set; }

        /// <summary>열림 연출 중 닫기 요청이 들어와 열림 완료 후 닫아야 함을 표시.</summary>
        internal bool CloseRequested;

        /// <summary>자신을 소유 스택에서 닫는다. 스택에 속해 있지 않으면 무시.</summary>
        public void Close() => Stack?.Close(this);

        /// <summary>
        /// 이 팝업이 닫혀 스택에서 제거될 때까지 대기한다. 이미 닫혀 있으면 즉시 완료.
        /// 확인 다이얼로그 응답 대기, 보상 연출 체인 등에 쓴다.
        /// </summary>
        public UniTask WaitUntilClosedAsync()
        {
            if (Phase == PopupPhase.None)
                return UniTask.CompletedTask;

            _closedSource ??= new UniTaskCompletionSource();
            return _closedSource.Task;
        }

        /// <summary>
        /// 열림 연출 대기 지점. 스택은 이 태스크가 끝나야 <see cref="PopupPhase.Open"/>으로 전이한다.
        /// 기본은 즉시 완료(연출 없음).
        /// </summary>
        protected internal virtual UniTask PlayOpenAsync(CancellationToken cancellationToken)
            => UniTask.CompletedTask;

        /// <summary>
        /// 닫힘 연출 대기 지점. 스택은 이 태스크가 끝나야 인스턴스를 해제한다.
        /// 기본은 즉시 완료(연출 없음).
        /// </summary>
        protected internal virtual UniTask PlayCloseAsync(CancellationToken cancellationToken)
            => UniTask.CompletedTask;

        /// <summary>
        /// back 키(ESC/Android back)가 이 팝업에 라우팅됐을 때. <c>true</c>(기본)면 닫기가
        /// 진행되고, <c>false</c>면 닫기를 거부한다(키 입력 자체는 소비됨).
        /// </summary>
        protected internal virtual bool OnBackRequested() => true;

        internal void Attach(PopupStack stack, PopupKey key, int layer)
        {
            Stack = stack;
            Key = key;
            Layer = layer;
            Phase = PopupPhase.Opening;
            CloseRequested = false;
        }

        internal void SetPhase(PopupPhase phase) => Phase = phase;

        internal void Detach()
        {
            Phase = PopupPhase.None;
            Stack = null;

            var source = _closedSource;
            _closedSource = null;
            source?.TrySetResult();
        }
    }
}
