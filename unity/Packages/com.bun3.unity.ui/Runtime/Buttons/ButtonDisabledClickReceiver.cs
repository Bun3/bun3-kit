using Bun3.Unity.Core.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.Unity.UI.Buttons
{
    /// <summary>
    /// Holds the disabled reason decided by <see cref="ButtonInteractableScope"/> and replays it
    /// when the disabled button is clicked.
    /// </summary>
    /// <remarks>
    /// <see cref="Selectable.interactable"/> being false does not block raycasts — only
    /// <see cref="Button.OnPointerClick"/> becomes a no-op, and the EventSystem still delivers
    /// events to other <see cref="IPointerClickHandler"/> implementations on the same GameObject.
    ///
    /// Auto-attached by the scope when needed. Never add or manipulate it manually.
    ///
    /// The replay condition is <b>"is the button non-interactable right now"</b>, not "did this
    /// scope disable it". So a click while disabled for another cause (directly assigned
    /// <see cref="Selectable.interactable"/>, a blocking parent <see cref="CanvasGroup"/>, etc.)
    /// still replays the last stored reason. This is intentional, and safe only under the premise
    /// that <b>one place decides the button's <see cref="Selectable.interactable"/> every
    /// frame</b>; break it and the reason can diverge from the actual cause.
    /// </remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class ButtonDisabledClickReceiver : MonoBehaviour, IPointerClickHandler
    {
        private Button _button;
        private DisabledReason _reason;
        private IButtonDisabledHandler _handler;

        internal void Set(Button button, DisabledReason reason, IButtonDisabledHandler handler)
        {
            _button = button;
            _reason = reason;
            _handler = handler;
        }

        internal void Clear()
        {
            _reason = default;
            _handler = null;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (!_button || _button.IsInteractable())
                return;

            if (_reason.IsEmpty)
                return;

            if (_handler.IsNull())
                return;

            _handler.Handle(_reason);
        }
    }
}
