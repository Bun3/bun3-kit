# Inspector sorting layer field

Provide a reusable Unity UPM package, `com.bun3.unity.inspector` 0.1.0, under the existing `unity/Packages` convention. Scope is a runtime `SortingLayerAttribute` and Editor-only drawer; no rendering runtime extraction.

The field supports serialized int IDs and string names. Options come from the consuming project's sorting layers. Unknown values remain visible and unchanged until an explicit selection. Mixed values, Undo, and prefab overrides use SerializedProperty. IMGUI and UI Toolkit use the same drawer behavior. No external dependencies or game-specific assumptions.

Acceptance: compile in Unity 2022.3; verify valid and missing IDs/names, mixed multi-edit, Undo and prefab overrides, and both Inspector backends. Existing serialization must remain unchanged when merely inspected.
