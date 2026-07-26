# Unity Core

Bun3 shared toolkit for Unity. General-purpose utilities used across Bun3 packages.

## Features

- **`Bun3.Unity.Core.Attributes.ReadOnlyAttribute`** — make a serialized field non-editable in the inspector.
- **`UnifiedToggleGroup`** — preset-based unified toggle that produces identical results in editor and runtime, with custom-extensible options and cascading control of nested groups. Built-in implementations cover `CanvasGroup`, `Image`, `LayoutElement`, `GameObject` activation, and another `UnifiedToggleGroup` (for cascading).
- **`Bun3.Common.Threading.CancellationScope`** — a structured cancellation-lifetime scope (a linked `CancellationTokenSource` in disposable form) for presentation/cutscene/staged-UI sequences, shipped in the shared `com.bun3.common` package. Cancelling a parent scope cancels every child; the core type is BCL-only, with `MonoBehaviour.CreateCancellationScope()` and a UniTask `Run(...)` provided as extensions in `Bun3.Unity.Core.Threading` (`CancellationScopeExtensions`). UniTask cancellation runs `try/finally` cleanup, so interrupted sequences leave consistent state.

## Requirements

- Unity 6000.3 (6.0) or later
- `com.mackysoft.serializereference-extensions` (declared as git dependency)
- `com.bun3.common` 0.1.0 (declared as a package dependency)

## Installation

Install via the Unity Package Manager:

- *Window → Package Manager → Add package from git URL...*
- *URL:* https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.core

Or add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.bun3.unity.core": "0.3.0",
    "com.bun3.common": "0.1.0"
  }
}
```

> `com.bun3.common` is not published to any UPM registry, so external consumers must also add its git URL manually (e.g. `https://github.com/Bun3/bun3-kit.git?path=dotnet/src/com.bun3.common`) — the `"com.bun3.common": "0.1.0"` dependency entry above cannot be resolved otherwise.

## Quick Start — UnifiedToggleGroup

```csharp
using Bun3.Unity.Core.UnifiedToggle;

// In the inspector, configure UnifiedToggleGroup with presets like ["Off", "On"]
// and add UnifiedToggle* children with options per preset.

// Apply a preset from code:
group.SetValue("On");

// Or toggle the last/first preset:
group.SetOn(true);
```

When a preset is applied, every registered toggle runs its options for that preset. The same code path is invoked when the group's preset buttons are clicked in the inspector, so edit-time and runtime yield identical results.

A complete example is included as the `Unified Toggle Group` sample.

## Quick Start — CancellationScope

```csharp
using Bun3.Common.Threading;
using Bun3.Unity.Core.Threading;
using Cysharp.Threading.Tasks;

private CancellationScope _sequenceScope;

private void PlaySequence()
{
    // Re-trigger safely: cancel the previous run. Its try/finally cleanup still executes.
    _sequenceScope?.Dispose();
    _sequenceScope = this.CreateCancellationScope();   // extension: cancels when this component is destroyed
    RunAsync(_sequenceScope).Forget();
}

private async UniTask RunAsync(CancellationScope scope)
{
    var ct = scope.Token;
    try
    {
        // Fire-and-forget background beats bound to the scope (Run is a UniTask extension):
        scope.Run(t => PlayVfxAsync(t));

        // Run beats to completion, in order:
        await StepAAsync(ct);
        await StepBAsync(ct);

        // Or in parallel, waiting for all before continuing:
        await UniTask.WhenAll(StepCAsync(ct), StepDAsync(ct));
    }
    finally
    {
        // Runs on normal completion, cancellation (re-trigger), and owner destruction.
        RestoreToBaseline();
    }
}
```

The `CancellationScope` type (`Bun3.Common.Threading`, package `com.bun3.common`) is BCL-only; `CreateCancellationScope()` (Unity) and `Run(...)` (UniTask) come from `CancellationScopeExtensions` in `Bun3.Unity.Core.Threading`. Cancellation is cooperative — forward `scope.Token` to every inner `await` so it propagates promptly. Use `scope.CreateChild()` to nest a sub-sequence that the parent can cancel as a unit. Outside a `MonoBehaviour`, root a scope with `CancellationScope.Create(parentToken)`.

## Links

- [Documentation](Documentation/unity.core.md)
- [Changelog](CHANGELOG.md)
- [Third Party Notices](Third%20Party%20Notices.md)
