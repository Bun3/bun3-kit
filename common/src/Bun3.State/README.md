# Bun3.State

`Bun3.State` 0.1.0 provides generated tracking for mutable game state. The package targets netstandard2.1 and includes a C# source generator and analyzer as a build asset. A consumer needs only a package reference:

```xml
<PackageReference Include="Bun3.State" Version="0.1.0" />
```

Declare a namespace-level, non-generic, sealed partial class. Mark each mutable instance field that belongs to the state with `[Tracked]`, and use the generated properties after initialization:

```csharp
using Bun3.State;

namespace Game
{
    [TrackedState]
    public sealed partial class EquipmentState
    {
        [Tracked] private int _level = 1;
        [Tracked] private EquipmentOptionState _option = new EquipmentOptionState();
    }

    [TrackedState]
    public sealed partial class EquipmentOptionState
    {
        [Tracked] private readonly TrackedList<int> _rerollLevels = new TrackedList<int>();
    }
}
```

```csharp
var state = new EquipmentState();
state.Level = 5;
state.Option.RerollLevels.Add(2);
```

The generated setter skips equal values, records the previous typed value when an owner has an active transaction, updates the field, and then marks a connected item changed. A rollback restores each change in reverse order through generated field writes, without invoking public setters or game callbacks. A transaction is optional: unattached or inactive state changes work normally and create no Undo record. Initial graph validation and the first growth of journal buffers have separate costs.

When a generated root is assigned to `ItemInstance<TState>.State`, Items connects its nested nodes to that item. Mutating a connected generated property or tracked list marks the item dirty without `BeginStateChange()` or a snapshot delegate. A removed node keeps its journal affiliation for the active transaction: edits through an external reference can still be undone; a detached edit alone does not mark the item dirty. Completion releases detached ownership. The same instance cannot mix generated tracking with `BeginStateChange()` at any point in its lifetime, including through null or opaque state replacements. Existing opaque state instances retain their explicit edit scope and independent-copy contract.

Game code maps generated state to its own DB, JSON, or protobuf DTOs. The generator does not make arbitrary protobuf objects tracked or preserve unknown wire fields. A mapping that needs unknown-field preservation must retain a DTO copy as separate, node-associated metadata and merge known fields on save. This is a loading and persistence cost, separate from Undo. Generated state rollback avoids a deep option copy, while the current inventory transaction still shallow-copies its membership and regeneration-basis dictionaries on first participation.

`DrainChanges` transfers a save buffer; it is not a database acknowledgement. Keep that buffer until the write succeeds. If a save succeeds while newer changes remain, mark the player dirty again for the next save. See [the Korean usage guide](https://github.com/Bun3/bun3-kit/blob/main/docs/generated-option-state.md) for mapping, rollback, measurement, and validation details.
