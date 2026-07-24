using System;
using UnityEngine;
using UnityEngine.UI;

namespace Bun3.UI.Buttons
{
    /// <summary>
    /// 여러 조건을 모아 버튼의 <see cref="Selectable.interactable"/>을 결정한다.
    /// 조건이 실패하면 사유를 보관해 두었다가, 사용자가 그 버튼을 클릭할 때 재생한다.
    /// </summary>
    public ref struct ButtonInteractableScope
    {
        private sealed class NullHandler : IButtonDisabledHandler
        {
            public static readonly NullHandler Instance = new NullHandler();

            public void Handle(DisabledReason reason) { }
        }

        private static IButtonDisabledHandler _defaultHandler = NullHandler.Instance;

        /// <summary>
        /// 생성자에 핸들러를 주지 않았을 때 쓰이는 핸들러.
        /// null을 대입하면 아무 것도 하지 않는 기본 핸들러로 되돌아간다.
        /// </summary>
        public static IButtonDisabledHandler DefaultHandler
        {
            get => _defaultHandler;
            set => _defaultHandler = value ?? NullHandler.Instance;
        }

        private readonly Button _button;
        private readonly IButtonDisabledHandler _handler;

        private bool _interactable;
        private DisabledReason _reason;
        private bool _disposed;

        public ButtonInteractableScope(Button button, IButtonDisabledHandler handler = null)
        {
            _button = button;
            _handler = handler ?? DefaultHandler;

            _interactable = true;
            _reason = default;
            _disposed = false;
        }

        /// <summary>
        /// 조건을 누적한다. 하나라도 실패하면 버튼은 비활성화된다.
        /// </summary>
        /// <param name="disabledMessage">
        /// 실패 사유 메시지. null이면 사유 없이 조용히 비활성화된다.
        /// </param>
        /// <remarks>
        /// 여러 조건이 함께 실패하면 <b>사유를 동반한 첫 실패</b>가 이긴다.
        /// 선언 순서가 곧 우선순위다. 사유 없이 <c>Require(false)</c>만 호출하면
        /// 버튼은 비활성화되지만 사유 슬롯은 비어 있어, 뒤따르는 조건의 사유가 채택된다.
        /// </remarks>
        public void Require(bool condition, string disabledMessage = null)
        {
            _interactable &= condition;

            if (!condition && _reason.IsEmpty && disabledMessage != null)
                _reason = new DisabledReason(disabledMessage);
        }

        /// <summary>
        /// 조건을 누적한다. 하나라도 실패하면 버튼은 비활성화된다.
        /// </summary>
        /// <param name="disabledAction">
        /// 비활성 버튼이 클릭됐을 때 실행할 동작.
        /// </param>
        /// <remarks>
        /// 여러 조건이 함께 실패하면 사유를 동반한 첫 실패가 이긴다.
        /// <br/>
        /// 매 프레임 호출되는 곳에서 메서드 그룹(<c>Require(cond, OpenPopup)</c>)을 넘기면
        /// 프레임마다 델리게이트가 할당된다. <see cref="Action"/> 필드에 한 번 캐싱해 넘길 것.
        /// </remarks>
        public void Require(bool condition, Action disabledAction)
        {
            _interactable &= condition;

            if (!condition && _reason.IsEmpty && disabledAction != null)
                _reason = new DisabledReason(disabledAction);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (!_button)
                return;

            _button.interactable = _interactable;

            // 에디터에서 컴포넌트가 씬/프리팹에 저장되는 것을 막는다.
            if (!Application.isPlaying)
                return;

            if (_reason.IsEmpty)
            {
                if (_button.TryGetComponent(out ButtonDisabledClickReceiver existing))
                    existing.Clear();

                return;
            }

            if (!_button.TryGetComponent(out ButtonDisabledClickReceiver receiver))
                receiver = _button.gameObject.AddComponent<ButtonDisabledClickReceiver>();

            receiver.Set(_button, _reason, _handler);
        }
    }
}
