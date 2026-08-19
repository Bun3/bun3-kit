using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Optional component that detects the back key each frame (ESC/Android back — both arrive
    /// as the escape key) and routes it to <see cref="PopupStack.HandleBack"/>.
    /// </summary>
    /// <remarks>
    /// The game must inject <see cref="Stack"/>. When the stack did not consume the key
    /// (no popups open), continue via <see cref="BackUnhandled"/> — e.g. quit confirmation.
    /// Supports both the Input System (<c>ENABLE_INPUT_SYSTEM</c>) and the legacy Input Manager.
    /// </remarks>
    public sealed class PopupBackKeyRouter : MonoBehaviour
    {
        /// <summary>Target stack, injected by the game. Null does nothing.</summary>
        public PopupStack Stack { get; set; }

        /// <summary>Fired when the back key was pressed but the stack was empty and did not consume it.</summary>
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
