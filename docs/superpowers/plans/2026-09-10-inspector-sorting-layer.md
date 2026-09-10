# Inspector sorting layer implementation

1. Add package manifest and separate runtime/editor assemblies with stable metadata.
2. Add the field attribute and dropdown drawer. Preserve unresolved/mixed values until explicit selection; write through SerializedProperty.
3. Document consumer usage and supported Inspector backends.
4. Integrate in the consuming project without renaming serialized fields, compile, and validate editing behavior. The parent integration task owns Unity execution and final verification.

## Verification

- Unity 2022.3.62f2 resolved the package through Client.Add as version 0.1.0 from the local bun3-kit checkout.
- Both SharedBurstEmitter integer and MeshSortingLayer string fields reference the package attribute. Their serialized field names remain unchanged.
- Skill_107252's stored ID 1673029844 still resolves to Fx; the UI Toolkit drawer constructs successfully.
- Independent static review found no actionable defects. Runtime and Editor assemblies are separated.
- Interactive dropdown selection, Undo and prefab override clicks were not exercised; their handling was reviewed through SerializedProperty/PropertyScope. No combat capture was required for this Inspector-only change.
- Consumer uses a relative local dependency until the package is published; another checkout needs the same sibling repository layout or an updated package reference.
