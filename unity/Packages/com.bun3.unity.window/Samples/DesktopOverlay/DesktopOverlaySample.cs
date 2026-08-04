using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Bun3.Unity.Window.Samples.DesktopOverlay
{
    /// <summary>
    /// The overlay itself needs no scene objects — it is configured by the
    /// <c>Resources/Bun3WindowOverlaySettings</c> asset and driven by the package's
    /// player-loop tick. This component only adds the one thing every borderless
    /// click-through overlay must provide itself: a way to quit (Esc).
    /// </summary>
    public sealed class DesktopOverlaySample : MonoBehaviour
    {
        private void Update()
        {
            if (QuitPressed())
            {
                Application.Quit();
            }
        }

        private static bool QuitPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
