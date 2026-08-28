using UnityEngine;

namespace Bun3.Unity.Window
{
    /// <summary>
    /// Startup configuration for the overlay features. Create via
    /// <c>Assets → Create → Bun3 → Window Overlay Settings</c> and place it in any
    /// <c>Resources</c> folder as <c>Bun3WindowOverlaySettings</c> — the bootstrap loads
    /// it once after the first scene. No asset means every feature starts off and the
    /// static APIs remain fully usable at runtime; the asset only sets initial state.
    /// </summary>
    [CreateAssetMenu(fileName = ResourceName, menuName = "Bun3/Window Overlay Settings")]
    public sealed class WindowOverlaySettings : ScriptableObject
    {
        public const string ResourceName = "Bun3WindowOverlaySettings";

        [Header("Window (build only)")]
        [SerializeField]
        [Tooltip("Size the window to its monitor's work area (excludes the taskbar) on startup - the standard desktop-overlay shape. Win32 resize only; Screen.SetResolution would wipe DWM transparency.")]
        private bool _fitToWorkArea;

        [SerializeField]
        [Tooltip("Keep Input System devices alive without window focus. An overlay almost never has focus, and without this the mouse freezes - starving pointer-driven click-through and UI clicks.")]
        private bool _backgroundInput = true;

        [Header("Always On Top")]
        [SerializeField]
        [Tooltip("Pin the window above all non-topmost windows on startup.")]
        private bool _alwaysOnTop = true;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Seconds between pin-drift enforcement checks (unscaled). 0 checks every frame.")]
        private float _enforceInterval = 0.25f;

        [SerializeField]
        [Tooltip("Freeze all window writes while another app confines the cursor (games locking the mouse) - any SetWindowPos/style write breaks their confinement. Normal behavior resumes when the clip ends.")]
        private bool _yieldToCursorClip = true;

        [Header("Transparency (build only)")]
        [SerializeField]
        [Tooltip("Make undrawn pixels show the desktop behind the window on startup. Uses Camera.main.")]
        private bool _transparency = true;

        [SerializeField]
        [Tooltip("Auto: DWM per-pixel alpha with color-key fallback. ColorKey: force the fallback.")]
        private TransparencyPreference _preferredMethod = TransparencyPreference.Auto;

        [SerializeField]
        [Tooltip("Color treated as fully transparent in ColorKey mode. Pick one that never appears in game art.")]
        private Color _colorKey = new(1f, 0f, 1f, 1f);

        [Header("Click-Through (build only)")]
        [SerializeField]
        [Tooltip("Let clicks pass through except when the pointer is over interactive content.")]
        private bool _autoClickThrough = true;

        [SerializeReference]
        [SubclassSelector]
        [Tooltip("What counts as interactive content under the pointer.")]
        private IPointerHitTest _hitTest = new EventSystemHitTest();

        public bool FitToWorkArea => _fitToWorkArea;
        public bool BackgroundInput => _backgroundInput;
        public bool AlwaysOnTopEnabled => _alwaysOnTop;
        public float EnforceInterval => _enforceInterval;
        public bool YieldToCursorClip => _yieldToCursorClip;
        public bool TransparencyEnabled => _transparency;
        public TransparencyPreference PreferredMethod => _preferredMethod;
        public Color ColorKey => _colorKey;
        public bool AutoClickThrough => _autoClickThrough;
        public IPointerHitTest HitTest => _hitTest;
    }
}
