# Window Transparency & Click-Through Design

> **Revision (2026-08-04, post-implementation):** the scene-component layer described in
> decisions 1 and 6 was dropped after review. Window state is process-global, so the
> MonoBehaviour drivers only added scene coupling and multi-instance ambiguity (two
> `ClickThroughDriver`s fight). Shipped architecture: **fully static facades**, startup
> config via a `WindowOverlaySettings` asset in `Resources`, and one player-loop tick
> (`WindowOverlayBootstrap` + `PlayerLoopSystemHelper`, added to `com.bun3.unity.core`
> 0.4.0, adapted from PlayerLoopInterface) doing pin enforcement + click-through policy.
> Click-through policy knobs live on the `ClickThrough` facade; both switches off =
> manual control. UniTask dependency removed (the enforcement loop was its only use).
> Everything else below (native mechanics, editor policy, hit-test design, validator,
> phasing) shipped as designed.

**Date:** 2026-08-04
**Target:** `unity/Packages/com.bun3.unity.window` (Unity 6000.3, URP 17.3, Windows player)
**Origin:** BongoCat decompiled implementation (`WinTransparentWindow.cs`, `TransparentWindow.cs`) + existing `AlwaysOnTop` module.

## Goal

Complete the desktop-overlay triad in `com.bun3.unity.window`:

| Feature | Status | Role |
| --- | --- | --- |
| Always-on-top | shipped (0.1.0) | window never hidden behind other apps |
| **Transparency** | this design | pixels the game doesn't draw show the desktop behind |
| **Click-through** | this design | mouse events pass through to the app below, except over interactive content |

All three combined = BongoCat-style overlay: a character floating on the desktop that
never blocks the user's work.

## What BongoCat does (evidence)

- **Transparency, DWM path (default):** style → `WS_POPUP | WS_VISIBLE` (border/titlebar
  removed), `DwmExtendFrameIntoClientArea(hwnd, margins(-1))`, camera clears to
  `Color.clear`, then `SetWindowPos(..., SWP_FRAMECHANGED | SWP_SHOWWINDOW)`. Per-pixel
  alpha, clean edges.
- **Transparency, color-key path (fallback / toggle):** `WS_EX_LAYERED` +
  `SetLayeredWindowAttributes(0x929292, 255, LWA_COLORKEY)`, camera clears to the key
  color. Binary cutout (no partial alpha), works where DWM alpha fails. Switching
  color-key → DWM at runtime is not attempted — BongoCat restarts the app.
- **Click-through driver:** every frame, `EventSystem.RaycastAll` at
  `Input.mousePosition`; mouse over nothing interactive → `WS_EX_TRANSPARENT | WS_EX_LAYERED`
  on, over something → off. A "gaming mode" forces click-through unconditionally.
  (BongoCat rewrites the style bits every frame and allocates a `PointerEventData` +
  `List` per frame — both avoidable.)
- **Extras (not this design's core):** taskbar icon hide via `WS_EX_TOOLWINDOW`,
  move-to-monitor via `EnumDisplayMonitors`.

## Decisions

1. **Same shape as AlwaysOnTop: static facade + scene behaviour.**
   - `WindowTransparency` (static) + `TransparentWindowBehaviour` (applies at startup).
   - `ClickThrough` (static, dumb toggle) + `ClickThroughDriver` (per-frame policy).
   - Composition over orchestration: an overlay scene stacks `AlwaysOnTopBehaviour` +
     `TransparentWindowBehaviour` + `ClickThroughDriver` on one GameObject. No
     "OverlayWindowBehaviour" umbrella — each feature stays independently usable.

2. **Editor is a hard no-op for both features** (unlike AlwaysOnTop, which pins the
   editor window on purpose). A transparent editor is broken UX; a click-through editor
   window is **unusable — you could no longer click your own editor**. `IsSupported` is
   false in the editor; state is still tracked so game UI code runs unchanged. In-editor
   development happens against the tracked state and the hit-test layer (fully testable);
   visual verification requires a build (checklist + sample provided).

3. **Transparency is apply-once at startup.** `WindowTransparency.Apply(preferred)` tries
   DWM unless color-key is forced, falls back to color-key on DWM failure, records
   `ActiveMethod` (`None | Dwm | ColorKey`). No runtime method switching (BongoCat
   restarts the app for one direction; we document "changing method requires restart"
   and keep the API honest: `Apply` after success returns the already-active method).
   Camera setup (clear flags + background color) is part of `Apply` via a passed
   `Camera` (behaviour serializes one; defaults to `Camera.main`).

4. **Click-through style writes are cached.** `ClickThrough.SetEnabled` early-outs when
   the requested state matches, so the per-frame driver costs one raycast most frames,
   not one `SetWindowLong` (BongoCat rewrites every frame).

5. **Hit testing is a pluggable, position-parameterized interface.**
   ```csharp
   public interface IPointerHitTest
   {
       bool IsHit(Vector2 screenPosition);   // position injected → unit testable
   }
   ```
   Default implementation `EventSystemHitTest` (BongoCat-proven): `RaycastAll` against
   `EventSystem.current` with a serialized ignore-layer mask, reusing one cached
   `PointerEventData` + `List<RaycastResult>` (zero steady-state alloc). Selected on the
   driver via `[SerializeReference, SubclassSelector]` (SerializeReferenceExtensions is
   already a toolkit baseline dependency). Covers uGUI out of the box and
   sprites/colliders when a `Physics2DRaycaster` sits on the camera.

6. **Driver policy mirrors BongoCat's, made explicit:**
   ```
   effective = ForceClickThrough || (AutoByPointer && !hitTest.IsHit(pointerPos))
   ```
   - `ForceClickThrough` (bool property) — "gaming mode" equivalent.
   - `AutoByPointer` (serialized, default on) — per-frame pointer polling.
   - Pointer position read via `Input.mousePosition` behind a small internal
     `Func<Vector2>` seam (tests inject positions; project uses the Input System package,
     but `Input.mousePosition` works with both backends — revisit if old input gets
     disabled).
   - Cadence: every frame in `Update()` (it's a cheap raycast; interval knob not needed).

7. **Shared window-handle resolution is extracted** from `AlwaysOnTop` into an internal
   static `GameWindow` (cached hwnd, `IsWindow` staleness re-resolve, player vs editor
   strategy, taskbar hwnd). `AlwaysOnTop`, `WindowTransparency`, `ClickThrough` all use
   it. Pure refactor; `AlwaysOnTop` public API unchanged.

8. **Win32 layer grows, stays internal, same `#if` scheme.** Added to `Win32Native`
   (user32 unless noted): `SetWindowLongPtr`/`GetWindowLongPtr` (64-bit-safe; existing
   32-bit `GetWindowLong` stays for the EXSTYLE topmost check),
   `SetLayeredWindowAttributes`, `DwmExtendFrameIntoClientArea` (dwmapi, `Margins`
   struct), plus constants `WS_POPUP`, `WS_VISIBLE`, `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`,
   `WS_EX_TOOLWINDOW`, `LWA_COLORKEY`, `SWP_FRAMECHANGED`, `SWP_SHOWWINDOW`.
   Transparency/click-through native paths compile under
   `UNITY_STANDALONE_WIN && !UNITY_EDITOR` only (no `UNITY_EDITOR_WIN` arm — decision 2).

9. **Project-settings validator ships in `Editor/`.** Transparency silently fails or
   renders black when player settings disagree; a validator turns that into actionable
   warnings. Checks (menu `Bun3/Window/Validate Overlay Settings` + build preprocess
   warnings via `IPreprocessBuildWithReport`):
   - Fullscreen mode = Windowed (exclusive fullscreen bypasses DWM)
   - Run In Background = on (overlay must animate unfocused)
   - D3D11 + "Use DXGI Flip Model Swapchain" **off** — flip-model presentation is
     believed to discard the alpha channel before DWM composition (⚠ verify on
     hardware, see Risks)
   - HDR display output off; warn that URP post-processing may not preserve alpha
   - Resizable Window off (recommended)

10. **Color key is serialized, default magenta `(1, 0, 1)`.** BongoCat hardcodes gray
    `0x929292` (tuned to their art). Magenta is the classic "never in art" key; fringe
    on edges is inherent to color keying and documented.

11. **Out of scope (future):** taskbar icon hide (`WS_EX_TOOLWINDOW` — trivial,
    follow-up), move-to-monitor, borderless window dragging, macOS/Linux.

## Layout

```
unity/Packages/com.bun3.unity.window/
  Runtime/
    AlwaysOnTop/
      AlwaysOnTop.cs                  // refactored onto GameWindow
      AlwaysOnTopBehaviour.cs
    Common/
      GameWindow.cs                   // internal: hwnd resolve/cache (extracted)
      Win32Native.cs                  // moved from AlwaysOnTop/, extended
    Transparency/
      TransparencyMethod.cs           // None | Dwm | ColorKey (+ Auto for preference)
      WindowTransparency.cs           // static facade
      TransparentWindowBehaviour.cs   // apply-on-start driver
    ClickThrough/
      ClickThrough.cs                 // static facade, cached style writes
      ClickThroughDriver.cs           // per-frame policy component
      IPointerHitTest.cs
      EventSystemHitTest.cs
  Editor/
    Bun3.Unity.Window.Editor.asmdef
    OverlaySettingsValidator.cs
  Tests/Runtime/
    AlwaysOnTopTests.cs
    WindowTransparencyTests.cs        // state machine + no-op guarantees
    ClickThroughTests.cs              // policy truth table via injected hit test/pointer
    EventSystemHitTestTests.cs        // playmode: real EventSystem + canvas objects
  Samples~/DesktopOverlay/            // minimal overlay scene + build checklist README
```

New asmdef reference: none for Runtime beyond existing (`UniTask`) plus
`MackySoft.SerializeReferenceExtensions` (already in the project manifest; add to
package dependencies). Editor asmdef references Runtime.

## API sketches

```csharp
public enum TransparencyMethod { None, Dwm, ColorKey }

public static class WindowTransparency
{
    public static bool IsSupported { get; }              // Windows player only
    public static TransparencyMethod ActiveMethod { get; }
    public static event Action<TransparencyMethod> Applied;

    // Configures camera, applies preferred method, falls back Dwm→ColorKey.
    // Returns the method actually active (None on failure/unsupported).
    public static TransparencyMethod Apply(Camera camera, TransparencyMethod preferred,
                                           Color colorKey);
}

public static class ClickThrough
{
    public static bool IsSupported { get; }              // Windows player only
    public static bool IsEnabled { get; }                // requested, tracked everywhere
    public static event Action<bool> EnabledChanged;
    public static void SetEnabled(bool enabled);         // cached; no redundant writes
}

public sealed class ClickThroughDriver : MonoBehaviour
{
    public bool ForceClickThrough { get; set; }          // "gaming mode"
    // serialized: _autoByPointer = true, [SerializeReference] IPointerHitTest _hitTest
    //             = new EventSystemHitTest();
    // Update(): ClickThrough.SetEnabled(ForceClickThrough
    //           || (_autoByPointer && !_hitTest.IsHit(pointerPos)));
}
```

`TransparentWindowBehaviour`: serialized `Camera` (null → `Camera.main`), preferred
method (`Auto`), color key; calls `WindowTransparency.Apply` in `Start`.

## Testing

- **Editor/CI (automated):** facade state machines (tracking, event-once semantics,
  no-op safety when unsupported), driver policy truth table (force × auto × hit) with
  injected hit test and pointer positions, `EventSystemHitTest` against a real canvas in
  playmode tests, validator rule logic.
- **Build (manual, per release):** `Samples~/DesktopOverlay` scene + checklist in its
  README — desktop visible through empty pixels, character clickable, clicks land on
  apps behind empty areas, alt-tab/taskbar behavior, both methods (DWM + forced color
  key), interaction with always-on-top enforcement.

## Risks / verify on hardware

- **Flip-model swapchain vs DWM alpha** (decision 9): the constraint is community
  knowledge, not vendor-documented; Unity 6 may also default D3D12. First build test
  must confirm which graphics API / swapchain settings actually preserve alpha, and the
  validator rules get corrected to match reality.
- **URP post-processing alpha:** bloom/tonemapping may write alpha=1 over the frame.
  Mitigation: sample ships with post off; URP "alpha processing" option investigated
  during implementation.
- **`WS_POPUP` restyle side effects:** window loses border mid-session (intended) but
  also min/max/close affordances; overlay apps quit via tray/hotkey — sample
  demonstrates `Application.Quit` binding.
- **Click-through + focus:** with `WS_EX_TRANSPARENT` active the window cannot regain
  focus by clicking; keyboard-driven features must not assume focus. Documented.

## Phasing

1. **Refactor + native layer:** `Common/GameWindow`, move/extend `Win32Native`,
   `AlwaysOnTop` on top of it (no behavior change; existing tests must stay green).
2. **Transparency:** facade + behaviour + validator + tests.
3. **Click-through:** facade + driver + hit tests + tests.
4. **Sample + hardware verification pass:** overlay sample, build checklist run on a
   real machine, validator rules corrected, README/CHANGELOG, version → 0.2.0.
5. **Follow-ups (separate):** taskbar icon hide, move-to-monitor, window dragging.
