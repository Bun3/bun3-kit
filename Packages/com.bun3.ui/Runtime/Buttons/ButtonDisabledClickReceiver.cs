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
    ///
    /// 재생 조건은 "이 스코프가 비활성화했는가"가 아니라 <b>"버튼이 지금 상호작용
    /// 불가능한가"</b>다. 따라서 스코프가 아닌 다른 원인(직접 대입한
    /// <see cref="Selectable.interactable"/>, 상위 <see cref="CanvasGroup"/> 차단 등)으로
    /// 비활성화된 상태에서 클릭해도 마지막으로 보관된 사유가 재생된다. 이는 의도된 동작이며,
    /// <b>한 버튼의 <see cref="Selectable.interactable"/>을 한 곳에서 매 프레임 결정한다</b>는
    /// 전제 아래에서만 안전하다. 이 전제가 깨지면 사유가 실제 원인과 어긋날 수 있다.
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

            if (_reason.IsEmpty || _handler == null)
                return;

            // _handler는 인터페이스 타입이라 위의 null 검사가 UnityEngine.Object의
            // 오버로드된 == 연산자를 타지 않는다. MonoBehaviour 핸들러가 파괴돼도
            // 참조는 살아 있어 그 검사를 통과하므로, 여기서 별도로 걸러낸다.
            if (_handler is UnityEngine.Object handlerObject && !handlerObject)
                return;

            _handler.Handle(_reason);
        }
    }
}
