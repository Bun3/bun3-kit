using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.UI.Buttons
{
    /// <summary>
    /// <see cref="ButtonInteractableScope"/>가 결정한 비활성 사유를 보관하고,
    /// 비활성 버튼이 클릭되면 재생한다.
    /// </summary>
    /// <remarks>
    /// <see cref="Selectable.interactable"/>이 false여도 레이캐스트는 막히지 않는다.
    /// <see cref="Button.OnPointerClick"/>만 no-op이 될 뿐, EventSystem은 같은
    /// GameObject의 다른 <see cref="IPointerClickHandler"/> 구현체에 이벤트를 전달한다.
    ///
    /// 스코프가 필요할 때 자동으로 붙인다. 직접 추가하거나 조작할 필요는 없다.
    /// </remarks>
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

            if (_reason.IsEmpty || _handler == null)
                return;

            _handler.Handle(_reason);
        }
    }
}
