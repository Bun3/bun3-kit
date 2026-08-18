using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 매 프레임 back 키(ESC/Android back — 둘 다 escape 키로 들어온다)를 감지해
    /// <see cref="PopupStack.HandleBack"/>으로 라우팅하는 선택 컴포넌트.
    /// </summary>
    /// <remarks>
    /// 게임이 <see cref="Stack"/>을 주입해야 동작한다. 스택이 키를 소비하지 않았을 때
    /// (팝업이 하나도 없을 때)의 처리 — 종료 확인 등 — 는 <see cref="BackUnhandled"/>로 잇는다.
    /// Input System(<c>ENABLE_INPUT_SYSTEM</c>)과 레거시 Input Manager 둘 다 지원한다.
    /// </remarks>
    public sealed class PopupBackKeyRouter : MonoBehaviour
    {
        /// <summary>라우팅 대상 스택. 게임이 주입한다. null이면 아무것도 하지 않는다.</summary>
        public PopupStack Stack { get; set; }

        /// <summary>back 키가 눌렸지만 스택이 비어 소비되지 않았을 때 발화.</summary>
        public event System.Action BackUnhandled;

        private void Update()
        {
            if (Stack == null || !WasBackPressedThisFrame())
                return;

            if (!Stack.HandleBack())
                BackUnhandled?.Invoke();
        }

        private static bool WasBackPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }
    }
}
