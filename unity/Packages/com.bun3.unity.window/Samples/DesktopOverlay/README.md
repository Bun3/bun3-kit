# Desktop Overlay Sample

Minimal BongoCat-style overlay: always-on-top + transparent + click-through.
No scene wiring — everything is configured by one settings asset.

## Setup

1. **Assets → Create → Bun3 → Window Overlay Settings**, keep the default name
   `Bun3WindowOverlaySettings`, and place it in any `Resources` folder. The defaults
   (always-on-top on, transparency Auto, auto click-through on) are the full overlay.
2. Scene: a camera tagged `MainCamera`, an `EventSystem`, and something visible
   (a sprite or uGUI `Image`; give it a `GraphicRaycaster`/`Physics2DRaycaster` path so
   the click-through hit test can see it).
3. Add `DesktopOverlaySample` to any GameObject — it only binds Esc to quit.
4. Run **Bun3 → Window → Validate Overlay Settings** and fix every warning
   (Windowed, Run In Background on, D3D11, flip-model swapchain off, HDR off).
5. Build for Windows and run the exe. Keep post-processing off in the sample —
   bloom/tonemapping can overwrite the alpha channel DWM transparency depends on.

## Build verification checklist

Transparency and click-through cannot be verified in the editor — run this list against
a real build after changes to this package:

- [ ] Desktop/other apps visible through every pixel the game doesn't draw
- [ ] No window border or title bar
- [ ] Game window stays above other apps, including after clicking them
- [ ] Clicking the visible content hits the game (window becomes interactive)
- [ ] Clicking empty (transparent) areas lands on the app behind
- [ ] Taskbar clicks don't permanently push the overlay down (enforcement re-pins)
- [ ] Esc quits the app
- [ ] Repeat the transparency checks with `Preferred Method = ColorKey` in the settings
      asset (hard-edged cutout is expected there)
- [ ] Alt-Tab behavior acceptable (window may not take focus while click-through is active)
