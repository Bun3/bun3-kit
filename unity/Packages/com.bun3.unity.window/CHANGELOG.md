# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.1] - 2026-08-28

### Fixed

- The cursor-clip yield now also pauses pointer-driven click-through toggling — each
  `SetWindowLong` style write from a background window can drop a game's clip exactly
  like a z-order change did, so click-through is pinned on for the duration (verified
  against a live borderless-fullscreen game: the escapes came from the style toggles,
  not the pin). Restoring the pin after a clip ends now waits a 1s grace period so a
  game re-setting its clip during focus churn is not wiped mid-transition.

## [0.3.0] - 2026-08-28

### Added

- `AlwaysOnTop.YieldToCursorClip` (default **on**) — while another app confines the cursor
  with `ClipCursor` (games in borderless fullscreen keep the mouse on their monitor this
  way), the pin is released and the z-order left untouched: any `SetWindowPos` from a
  background window makes Windows drop the clip, letting the pointer escape onto other
  monitors mid-game. The pin restores automatically when the clip ends; `IsEnabled` is
  unaffected, and `IsYieldingToCursorClip` exposes the hold. Configurable via the
  `WindowOverlaySettings` asset (`Yield To Cursor Clip`).

## [0.2.1] - 2026-08-13

### Changed

- Moved the overlay settings validator menu to `Window/Validate Overlay Settings`.

## [0.2.0] - 2026-08-04

### Changed

- **Fully static architecture — the MonoBehaviour drivers are gone.** The window state is process-global, so the component layer (`AlwaysOnTopBehaviour`, `TransparentWindowBehaviour`, `ClickThroughDriver`) only added scene coupling and multi-instance ambiguity. Startup configuration now comes from a `WindowOverlaySettings` asset (`Resources/Bun3WindowOverlaySettings`, `[SerializeReference]` hit-test selection included); per-frame work (pin enforcement, click-through policy, focus re-assert) runs on a single player-loop tick inserted by `WindowOverlayBootstrap` via `com.bun3.unity.core`'s `PlayerLoopSystemHelper`. Click-through policy knobs moved onto the static facade (`ClickThrough.ForceClickThrough/AutoByPointer/HitTest`; both switches off = manual control). Enforcement cadence is `AlwaysOnTop.EnforceIntervalSeconds`.
- Dependency swap: `com.cysharp.unitask` removed (the enforcement loop was its only consumer); `com.bun3.unity.core` (0.4.0) added.

### Added

- `WindowTransparency` — window transparency for desktop overlays: borderless (`WS_POPUP`) + DWM glass extension with per-pixel alpha (camera cleared to alpha zero), falling back to layered-window color key (`LWA_COLORKEY`, serialized key color) when DWM fails or is bypassed by preference. Apply-once per process; Windows player only (never the editor).
- `ClickThrough` — cached `WS_EX_TRANSPARENT` toggle letting mouse events pass through to apps behind; preserves `WS_EX_LAYERED` when color-key transparency owns it. Windows player only — never the editor, which it would render unclickable. Policy `Force || (Auto && pointer not over interactive content)` with a pluggable `IPointerHitTest`, evaluated by the package tick.
- `WindowOverlaySettings` — startup configuration asset (`Resources/Bun3WindowOverlaySettings`) covering all three features, with `[SerializeReference, SubclassSelector]` hit-test selection.
- `WindowOverlayBootstrap` — `[RuntimeInitializeOnLoadMethod]` wiring: applies the settings asset and inserts the overlay player-loop tick (removed automatically on quit/play-mode exit).
- `EventSystemHitTest` — default hit test via `EventSystem.RaycastAll` with an ignored-layer mask and zero steady-state allocation; input read through the Input System (legacy fallback).
- `OverlaySettingsValidator` (Editor) — menu command and Windows build preprocess warnings for overlay-hostile player settings (non-Windowed, Run In Background off, non-D3D11, flip-model swapchain, HDR output, resizable window).
- `Desktop Overlay` sample — one-component overlay bootstrap plus the manual build verification checklist.
- Internal `GameWindow` — shared window-handle resolution extracted from `AlwaysOnTop`; `Win32Native` moved to `Runtime/Common/` and extended (style/layered-window/DWM interop).

## [0.1.0] - 2026-08-04

### Added

- `AlwaysOnTop` — static facade pinning the game window into the topmost z-order band (`SetWindowPos` + `HWND_TOPMOST`). Resolves the game's own window via `EnumThreadWindows` in the player (focus-independent) and the process main window in the editor, tracks requested state on every platform, and exposes `EnforceOnce()` to re-assert the pin when Windows drops it (lost `WS_EX_TOPMOST` bit, foreground change, taskbar foreground).
- Windows-editor support for in-editor development: the pin targets the editor main window during play mode and is automatically released when play mode exits. Non-Windows platforms remain safe no-ops.
- `AlwaysOnTopBehaviour` — scene driver with enable-on-start and a configurable-interval UniTask enforcement loop (unscaled time, cancelled via the component's destroy lifetime; also re-asserts on application focus gain).
- Runtime tests covering state tracking, event semantics, real pin/unpin verification where supported, no-op guarantees elsewhere, and behaviour lifecycle.
