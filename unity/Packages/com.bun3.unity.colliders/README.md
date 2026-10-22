# Bun3 Unity Colliders

Editor tools for 2D colliders, Unity 2022.3 or later. Editor-only, no runtime code and no external package dependencies.

## Installation

Add the package from git in `Packages/manifest.json`:

```json
"com.bun3.unity.colliders": "https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.colliders"
```

## Selecting and deleting collider points

Select a GameObject with a `PolygonCollider2D` or `EdgeCollider2D`, then pick **Select Polygon Collider Points** or **Select Edge Collider Points** in the Scene view Tools overlay.

| Input | Result |
|---|---|
| Click a point | Selects only that point |
| Shift or Ctrl/Cmd click | Toggles the point |
| Drag on empty space | Selects the points inside the box |
| Shift or Ctrl/Cmd drag | Adds the points inside the box |
| Edit > Delete (Delete, or Cmd+Backspace on macOS) | Deletes the selected points |
| Esc, or click empty space | Clears the selection |

Deletion is recorded with Undo. A polygon path left with fewer than 3 points is removed with them. When no polygon path, or fewer than 2 edge points, would remain, nothing changes and a warning is logged. With no point selected, Edit > Delete deletes the GameObject as usual.

The tool edits the active collider only. Moving and adding points stays with Unity's built-in Edit Collider.
