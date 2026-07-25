# About Unity Core

The Unity Core package (`com.bun3.core`) bundles general-purpose utilities used across Bun3 packages. The current release ships a `[ReadOnly]` attribute and a complete `UnifiedToggleGroup` system.

# Installing Unity Core

Install via the [Package Manager](https://docs.unity3d.com/Manual/upm-ui.html). The package depends on `com.mackysoft.serializereference-extensions` (declared as a git URL dependency); Unity resolves it automatically when the package is imported.

# Using Unity Core

## ReadOnlyAttribute

Apply `[ReadOnly]` to a serialized field to make it visible in the inspector but not editable.

```csharp
using Bun3.Core.Attributes;
using UnityEngine;

public class Example : MonoBehaviour
{
    [SerializeField, ReadOnly] private string _id;
}
```

## UnifiedToggleGroup

`UnifiedToggleGroup` lets you define a finite set of named **presets** for a UI panel (or any GameObject hierarchy) and switch between them with a single call. Each child toggle owns a list of **options** describing how its target component should look in each preset. Because the same invocation path is used in editor and at runtime, what you preview in the inspector is exactly what ships.

### Concept

```
UnifiedToggleGroup ───► presets: ["Off", "On"]   (or any string list)
       │
       ├─ UnifiedToggleCanvasGroup ──► CanvasGroup options per preset
       ├─ UnifiedToggleImage      ──► Image sprite/color options per preset
       ├─ UnifiedToggleLayoutElement ──► LayoutElement sizing per preset
       ├─ UnifiedToggleGameObject ──► which children to SetActive per preset
       └─ UnifiedToggleToggleGroup ──► cascade: pick another group's preset
```

A `UnifiedToggleToggleGroup` lets a parent group drive a child group, so you can build trees: e.g. an outer `Off/On` group whose `On` selects `Inner.A` and whose `Off` selects `Inner.C`.

### Setup workflow

1. Add `UnifiedToggleGroup` to a parent GameObject. Edit its `_presets` list.
2. On each child GameObject, add a `UnifiedToggle*` matching the component you want to drive.
3. Set the toggle's `_authorGroup` to the parent group (the toggle auto-registers in `OnEnable`).
4. Use the `[SubclassSelector]` dropdown on the toggle's `_options` field to add concrete option types (e.g. `UnifiedOptionImageSprite`). The inspector keeps option entries in sync with the group's presets.
5. Fill in each option's value per preset.
6. Use the preset buttons that appear in the group's inspector to preview presets at edit time. The current preset is highlighted in green.
7. At runtime, call `group.SetValue(presetName)` or `group.SetOn(bool)` to apply.

### API summary

| Type | Description |
|---|---|
| `UnifiedToggleGroup` | `MonoBehaviour` that owns the preset list and registered toggles. Entry point: `SetValue(string)`, `SetOn(bool)`, `UpdateValues()`. |
| `BaseUnifiedToggle` / `BaseUnifiedToggle<TComponent>` | Abstract `MonoBehaviour` parent of every toggle. Owns a list of options and auto-registers/unregisters with `_authorGroup`. |
| `IUnifiedOption<TComponent>` / `UnifiedOption<TComponent, TOption>` | Generic option base. Subclass it to define new option types. |
| `UnifiedToggleCanvasGroup` | Drives `CanvasGroup.alpha`, `interactable`, `blocksRaycasts`. |
| `UnifiedToggleImage` | Drives `Image.sprite`, `overrideSprite`, `color`. |
| `UnifiedToggleLayoutElement` | Drives `LayoutElement` sizing values. |
| `UnifiedToggleGameObject` | Toggles which child GameObjects are active per preset. |
| `UnifiedToggleToggleGroup` | Cascade: select a preset on another `UnifiedToggleGroup`. |

### Adding a new option type

```csharp
using Bun3.Core.UnifiedToggle;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class UnifiedOptionImageRaycastTarget : UnifiedOption<Image, bool>
{
    protected override bool GetDefaultOption() => true;

    protected override void SetOption(Image component, bool value)
    {
        component.raycastTarget = value;
    }
}
```

After compilation, the new type appears in the `[SubclassSelector]` dropdown for any `UnifiedToggleImage._options` field.

### Edit-time / runtime parity

`BaseUnifiedToggle` is decorated with `[ExecuteAlways]`, and the inspector's preset buttons call the same `SetValue`/`UpdateValues` path that runtime code uses. There is no separate "preview" mode — what you click is what runs.

# Technical details

## Requirements

- Unity 6000.3 (6.0) or later

## Package contents

| Location | Description |
|---|---|
| `Runtime/Attributes/ReadOnlyAttribute.cs` | `[ReadOnly]` attribute. |
| `Editor/Attributes/ReadOnlyDrawer.cs` | Drawer that grays out the field. |
| `Runtime/UnifiedToggleGroup/` | `UnifiedToggleGroup`, `IUnifiedToggle`, `BaseUnifiedToggle<>`, options framework, and built-in toggle implementations. |
| `Editor/UnifiedToggleGroup/` | Custom inspectors for the toggle group and base toggle. |
| `Samples/UnifiedToggleGroup/` | Sample MonoBehaviour and README. |

## Document revision history

| Date | Reason |
|---|---|
| 2026-05-09 | Document updated for package version 0.2.0. Added `ReadOnlyAttribute` and `UnifiedToggleGroup` sections. |
| 2026-05-08 | Document created. Matches package version 0.1.0. |
