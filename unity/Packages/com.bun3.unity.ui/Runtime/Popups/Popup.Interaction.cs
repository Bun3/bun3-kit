// Popup partial — 입력 보호·딤 클릭 담당.
using System;
using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Bun3.Unity.UI.Popups
{
    // 입력 보호: 전환 중 레이캐스트 차단, 열림 직후 오입력 유예, 딤 클릭 닫기,
    // 열림 시 EventSystem 선택 해제, 최상단 진입/이탈 훅.
    public abstract partial class Popup
    {
        [SerializeField]
        [Tooltip("열림/닫힘 연출 중 레이캐스트를 차단해 연타 오입력을 막는다.")]
        private bool _blockInteractionDuringTransition = true;

        [SerializeField]
        [Tooltip("열림 완료 후 추가로 입력을 무시할 시간(초, unscaled). 0이면 없음. 레거시 ignoreInteractDuration 대응.")]
        private float _postOpenInteractionDelay;

        [SerializeField]
        [Tooltip("딤 클릭 시 팝업을 닫는다(레거시 HideIfClickedOutside 대응). 닫기 잠금은 존중된다.")]
        private bool _closeOnDimClick;

        [SerializeField]
        [Tooltip("열릴 때 EventSystem 선택을 해제해 키보드/패드 오입력을 막는다.")]
        private bool _clearSelectionOnOpen = true;

        private CanvasGroup _interactionGroup;
        private bool _isTopmost;

        /// <summary>딤 클릭 닫기 여부. 딤(<see cref="BackgroundDim"/>)에 레이캐스트 타깃(Image 등)이 있어야 동작한다.</summary>
        public bool CloseOnDimClick
        {
            get => _closeOnDimClick;
            set => _closeOnDimClick = value;
        }

        /// <summary>
        /// 이 팝업이 전체 스택의 최상단이 됐을 때(처음 열릴 때 포함). 위 팝업이 닫혀
        /// 다시 드러난 경우도 여기로 온다 — 갱신/포커스 복원 지점. 기본은 아무것도 안 한다.
        /// </summary>
        protected virtual void OnBecameTopmost() { }

        /// <summary>다른 팝업이 위에 열려 최상단에서 벗어났을 때. 기본은 아무것도 안 한다.</summary>
        protected virtual void OnCovered() { }

        internal void UpdateTopmost(bool isTopmost)
        {
            if (_isTopmost == isTopmost)
                return;

            _isTopmost = isTopmost;

            if (Phase == PopupPhase.None)
                return;

            if (isTopmost)
                OnBecameTopmost();
            else
                OnCovered();
        }

        /// <summary>Opening/Closing 진입 시 스택이 호출 — 전환 중 오입력 차단.</summary>
        internal void OnTransitionStarted()
        {
            if (!_blockInteractionDuringTransition)
                return;

            _interactionGroup ??= gameObject.GetOrAdd<CanvasGroup>();
            _interactionGroup.blocksRaycasts = false;
        }

        /// <summary>Open 전이 시 스택이 호출 — 유예 시간 뒤 입력 복원.</summary>
        internal void OnOpenCompleted(CancellationToken cancellationToken)
        {
            if (!_blockInteractionDuringTransition)
                return;

            if (_postOpenInteractionDelay > 0f)
                RestoreInteractionAfterDelayAsync(cancellationToken).Forget();
            else if (_interactionGroup)
                _interactionGroup.blocksRaycasts = true;
        }

        private async UniTaskVoid RestoreInteractionAfterDelayAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_postOpenInteractionDelay),
                    ignoreTimeScale: true, cancellationToken: cancellationToken)
                .SuppressCancellationThrow();

            if (_interactionGroup && Phase == PopupPhase.Open)
                _interactionGroup.blocksRaycasts = true;
        }

        /// <summary>Attach 시 입력 보호 상태를 새 세션으로 초기화한다.</summary>
        private void SetUpInteractionForSession()
        {
            _isTopmost = false;

            if (_clearSelectionOnOpen && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(null);

            if (_closeOnDimClick && _backgroundDim)
                _backgroundDim.GetOrAdd<PopupDimClickCatcher>().Owner = this;
        }
    }

    /// <summary>
    /// 딤 클릭을 소유 팝업의 <see cref="Popup.Close"/>로 라우팅하는 내부 컴포넌트.
    /// <see cref="Popup.CloseOnDimClick"/>가 켜진 팝업의 딤에 자동으로 붙는다.
    /// </summary>
    internal sealed class PopupDimClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        internal Popup Owner;

        public void OnPointerClick(PointerEventData eventData) => Owner?.Close();
    }
}
