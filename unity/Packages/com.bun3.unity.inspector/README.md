# Bun3 Unity Inspector

Reusable Inspector attributes for Unity 2022.3 or later. No external package dependencies.

## Installation

In Package Manager, choose **Add package from disk** and select this package's `package.json`. For local development, keep the package in a sibling checkout and let Package Manager record the relative file dependency. No registry publication is required for local use.

## Sorting layers

Apply `[SortingLayer]` to a serialized `int` ID or `string` name field:

```csharp
using Bun3.Unity.Inspector;
using UnityEngine;

public sealed class EffectSettings : MonoBehaviour
{
    [SortingLayer] public int sortingLayerID;
    [SortingLayer] public string sortingLayerName = "Default";
}
```

The dropdown displays the current project's sorting layer names. Integer fields retain Unity's stable layer IDs, not list indices. String fields store the selected name. Existing field names and serialized values need no migration.

Deleted or unavailable layers display `Missing: <value>` and remain unchanged until a valid layer is selected. Mixed selections remain mixed until edited. Editing uses Unity serialized properties, supporting Undo, multi-object editing, and prefab overrides.

Both IMGUI and UI Toolkit Inspectors are supported; UI Toolkit hosts the same drawer in an `IMGUIContainer`. The runtime assembly contains only the attribute, and the drawer assembly is Editor-only. Custom assembly definitions using the attribute must reference `Bun3.Unity.Inspector`.

This attribute selects sorting layers, not GameObject layers or sorting orders. Unsupported field types display an error. Runtime validation and layer creation are outside its scope. Names stored in string fields become unresolved when renamed; prefer integer IDs when persistent identity is required.
