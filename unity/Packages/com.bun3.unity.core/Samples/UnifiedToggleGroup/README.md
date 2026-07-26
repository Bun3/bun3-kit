# Unified Toggle Group

Demonstrates `UnifiedToggleGroup` — a preset-based unified toggle that produces identical results in editor and runtime, with custom-extensible options and cascading control of nested groups.

## How it works

1. Add a `UnifiedToggleGroup` component to a GameObject. Configure its `_presets` (e.g. `["Off", "On"]` or `["A", "B", "C"]`).
2. On child GameObjects, add one of the built-in toggles:
   - `UnifiedToggleCanvasGroup` — drive `CanvasGroup.alpha`, `interactable`, `blocksRaycasts`.
   - `UnifiedToggleImage` — drive `Image.sprite`, `overrideSprite`, `color`.
   - `UnifiedToggleLayoutElement` — drive `LayoutElement` sizing values.
   - `UnifiedToggleGameObject` — toggle which child GameObjects are active per preset.
   - `UnifiedToggleToggleGroup` — make this toggle drive **another** `UnifiedToggleGroup`'s preset (cascading).
3. On each toggle, set `_authorGroup` to the parent group so it auto-registers.
4. Add option entries to the toggle's `_options` list using the `[SubclassSelector]` dropdown. Each option records a value per preset (the inspector keeps option entries in sync with the group's presets).
5. Click a preset button on the group's inspector to apply the preset immediately. Or call `group.SetValue("preset")` from code at runtime.

## Cascading groups

A `UnifiedToggleToggleGroup` lets one group drive another. Example:

- Outer group `Outer` has presets `["Off", "On"]`.
- Inner group `Inner` has presets `["A", "B", "C"]`.
- On a child GameObject of `Outer`, place a `UnifiedToggleToggleGroup` whose `_authorGroup = Outer` and whose `_group = Inner`.
- On its `_options`, add a `UnifiedOptionToggleGroup`. The inspector creates one entry per `Outer` preset (`Off`, `On`). Set `Off → "C"` and `On → "A"`.
- Now toggling `Outer` between `Off` and `On` will also drive `Inner` to `C` and `A` respectively.

## Files

- `UnifiedToggleGroupSample.cs` — example MonoBehaviour that drives a configured `UnifiedToggleGroup` from code (`SetValue`, `SetOn`, `Toggle`).
- `Bun3.Core.Samples.UnifiedToggleGroup.asmdef` — assembly definition for this sample.

## Try it

1. Create a Canvas with several `Image` / `CanvasGroup` / `LayoutElement` children.
2. Attach `UnifiedToggleGroup` to a parent. Define your presets.
3. Attach the matching `UnifiedToggle*` to each child and bind it to the group.
4. Add options to each toggle and edit per-preset values.
5. In the group's inspector, click each preset button to verify edit-time and runtime results match.
6. Add `UnifiedToggleGroupSample` to a GameObject, assign the group, and enter Play mode to see the same effect from code.
