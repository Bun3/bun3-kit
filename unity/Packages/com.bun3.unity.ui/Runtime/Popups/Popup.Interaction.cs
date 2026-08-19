// Popup partial — input guarding and dim clicks.
using System;
using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Bun3.Unity.UI.Popups
{
    // Input guarding: raycast blocking during transitions, post-open input grace period,
    // dim-click close, EventSystem deselection on open, topmost enter/leave hooks.
    public abstract partial class Popup
    {
        [SerializeField]
        [Tooltip("Block raycasts during open/close transitions to prevent rapid-tap misinputs.")]
        private bool _blockInteractionDuringTransition = true;

        [SerializeField]
        [Tooltip("Extra time to ignore input after the open completes (seconds, unscaled). 0 disables.")]
        private float _postOpenInteractionDelay;

        [SerializeField]
        [Tooltip("Close the popup when the dim is clicked. Close locks are respected.")]
        private bool _closeOnDimClick;

        [SerializeField]
        [Tooltip("Clear the EventSystem selection on open to prevent keyboard/gamepad misinputs.")]
        private bool _clearSelectionOnOpen = true;

        private CanvasGroup _interactionGroup;
        private bool _isTopmost;

        /// <summary>Whether dim clicks close the popup. The dim (<see cref="BackgroundDim"/>) needs a raycast target (e.g. Image).</summary>
        public bool CloseOnDimClick
        {
            get => _closeOnDimClick;
            set => _closeOnDimClick = value;
        }

        /// <summary>
        /// Called when this popup becomes topmost of the whole stack (including first open),
        /// and when re-exposed after a popup above closes — refresh/focus-restore point.
        /// Default does nothing.
        /// </summary>
        protected virtual void OnBecameTopmost() { }

        /// <summary>Called when another popup opens on top, leaving topmost. Default does nothing.</summary>
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

        /// <summary>Called by the stack on Opening/Closing entry — blocks misinputs during the transition.</summary>
        internal void OnTransitionStarted()
        {
            if (!_blockInteractionDuringTransition)
                return;

            _interactionGroup ??= gameObject.GetOrAdd<CanvasGroup>();
            _interactionGroup.blocksRaycasts = false;
        }

        /// <summary>Called by the stack on transition to Open — restores input after the grace period.</summary>
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

        /// <summary>Resets input-guard state for the new session on Attach.</summary>
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
    /// Internal component that routes dim clicks to the owner's <see cref="Popup.Close"/>.
    /// Attached automatically to the dim of popups with <see cref="Popup.CloseOnDimClick"/> enabled.
    /// </summary>
    internal sealed class PopupDimClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        internal Popup Owner;

        public void OnPointerClick(PointerEventData eventData) => Owner?.Close();
    }
}
