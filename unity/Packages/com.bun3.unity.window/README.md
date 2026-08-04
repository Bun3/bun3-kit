# Bun3 Unity Window

Desktop-overlay window control for Windows standalone builds. Three composable
features — enable all three for a BongoCat-style overlay:

| Feature | Facade | What it does |
| --- | --- | --- |
| Always-on-top | `AlwaysOnTop` | window never hidden behind other apps |
| Transparency | `WindowTransparency` | undrawn pixels show the desktop behind |
| Click-through | `ClickThrough` | clicks pass through except over your content |

Everything is **static — no scene objects**. Startup state comes from one
`WindowOverlaySettings` asset in `Resources`; per-frame work (pin enforcement,
click-through policy) runs on a single player-loop tick inserted by the package's
bootstrap (`PlayerLoopSystemHelper` from `com.bun3.unity.core`).

## Requirements

- Unity 6000.3+
- Dependencies (declared in package.json): `com.bun3.unity.core` (player-loop tick),
  [SerializeReferenceExtensions](https://github.com/mackysoft/Unity-SerializeReferenceExtensions)
  (pluggable hit test), uGUI (default hit test), Input System (pointer reading, with
  legacy fallback)
- Windows standalone player (Mono or IL2CPP). The whole API compiles everywhere and
  tracks requested state; native calls happen only where supported (see Editor behavior).
- **Windowed** or borderless mode — exclusive fullscreen bypasses the desktop
  compositor, so none of this applies there. Run **Bun3 → Window → Validate Overlay
  Settings** to catch hostile player settings (flip-model swapchain, HDR, etc.).

## Quick start — full overlay

1. **Assets → Create → Bun3 → Window Overlay Settings**, keep the name
   `Bun3WindowOverlaySettings`, put it in any `Resources` folder.
2. Scene needs a camera tagged `MainCamera`, an `EventSystem`, and your visible content
   (raycastable, so the hit test can see it).
3. Build for Windows. Defaults give the full overlay: pinned on top, transparent,
   click-through except over content.

No settings asset = every feature starts off; the static APIs below still work.

```csharp
using Bun3.Unity.Window;

// Runtime control from anywhere — no component references:
AlwaysOnTop.SetEnabled(on);                       // settings-menu toggle
AlwaysOnTop.EnabledChanged += ui.SetWithoutNotify;

ClickThrough.ForceClickThrough = gamingMode;      // ignore pointer, always pass through
ClickThrough.AutoByPointer = false;               // both off → manual SetEnabled control
ClickThrough.HitTest = new MyCustomHitTest();     // swap the policy's hit test

WindowTransparency.Apply(Camera.main,             // manual apply when not using the asset
    TransparencyPreference.Auto, colorKey);
```

## Editor behavior

- **Always-on-top works in the Windows editor**: it pins the editor main window during
  play mode (visual testing) and releases the pin when play mode exits.
- **Transparency and click-through never touch the editor** — a transparent editor is
  broken UX and a click-through editor could not be clicked again. They no-op in the
  editor (state still tracked); visual verification happens against a build using the
  sample's checklist. The hit-test/policy layer is fully unit-tested in the editor.
- Non-Windows platforms: everything is a safe no-op with state tracking, so UI code
  needs no platform branches.

## How it works

**Bootstrap.** `[RuntimeInitializeOnLoadMethod]` loads the settings asset, applies
initial state (transparency uses `Camera.main`), and inserts one tick before
`Update.ScriptRunBehaviourUpdate`. The tick enforces the topmost pin on an interval
(`AlwaysOnTop.EnforceIntervalSeconds`) and evaluates the click-through policy; focus
regain also re-asserts the pin. On `Application.quitting` (and editor play-mode exit)
the tick is removed automatically.

**Always-on-top** pins the window into the topmost z-band via
`SetWindowPos(HWND_TOPMOST)`. Windows silently drops the pin (taskbar clicks, competing
topmost windows, explorer restarts), so enforcement re-asserts on drift: lost
`WS_EX_TOPMOST` bit, foreground change, or taskbar foreground. The game's own window is
resolved focus-independently (`EnumThreadWindows`), cached, re-resolved when stale.

**Transparency** removes the border (`WS_POPUP`), extends the DWM glass frame across
the client area (`DwmExtendFrameIntoClientArea`), and clears the camera to alpha zero —
per-pixel alpha with smooth edges. If DWM fails (or `ColorKey` is forced), it falls
back to a layered-window color key: one configured color (default magenta) becomes
fully transparent, with a hard-edged cutout. Applied once per process; switching
methods needs a restart. Post-processing that overwrites alpha (bloom, tonemapping)
can break the DWM method — validate in a build.

**Click-through** toggles `WS_EX_TRANSPARENT` so mouse events skip the window. Policy:
`ForceClickThrough || (AutoByPointer && pointer not over interactive content)` — with
both switches off the tick leaves the state alone (manual `SetEnabled` control). The
default hit test raycasts through the `EventSystem` (uGUI out of the box; add a
`Physics2DRaycaster` to the camera for sprites/colliders) minus ignored layers, with
zero steady-state allocation. The toggle caches state, so steady frames cost one
raycast and zero native calls. While click-through is active the window cannot regain
focus by clicking — don't gate features on focus.

## API

| Member | Description |
| --- | --- |
| `WindowOverlaySettings` | Startup config asset (`Resources/Bun3WindowOverlaySettings`). |
| `AlwaysOnTop.SetEnabled / IsEnabled / EnabledChanged` | Topmost pin control and state. |
| `AlwaysOnTop.EnforceIntervalSeconds` | Enforcement cadence (unscaled; 0 = every frame). |
| `AlwaysOnTop.IsEffectivelyTopMost()` / `EnforceOnce()` | Actual OS state / manual drift re-assert. |
| `WindowTransparency.Apply(camera, preference, colorKey)` | Apply once; returns the active `TransparencyMethod`. |
| `WindowTransparency.ActiveMethod / Applied` | Which mechanism is live (`None`/`Dwm`/`ColorKey`). |
| `ClickThrough.SetEnabled / IsEnabled / EnabledChanged` | Raw pass-through toggle (cached). |
| `ClickThrough.ForceClickThrough / AutoByPointer / HitTest` | Policy knobs read by the tick. |
| `ClickThrough.TickPolicy()` | Evaluate-and-push, for callers scheduling it themselves. |
| `IPointerHitTest` / `EventSystemHitTest` | Pluggable "is the pointer over content?" test. |
| `*.IsSupported` | Whether the feature reaches the OS on this platform right now. |

All members are main-thread only.

## Limitations

- Windows only. macOS (`NSWindow.level`) and Linux (`_NET_WM_STATE_ABOVE`) are out of scope.
- Cannot stay above exclusive-fullscreen apps, the secure desktop (UAC), or other
  topmost windows that re-assert more aggressively.
- The flip-model-swapchain and HDR constraints on DWM transparency are encoded in the
  validator from community knowledge — treat the sample's build checklist as the source
  of truth on real hardware.
