# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-07-07

### Added

- `Bun3.Core.Threading.CancellationScope` — a structured cancellation-lifetime scope: a linked `CancellationTokenSource` in disposable form. Cancelling/disposing a scope cancels every child scope; `CreateChild()` nests scopes; `Create(CancellationToken)` roots one. The type itself depends only on the BCL.
- `CancellationScopeExtensions` — Unity/UniTask glue kept off the core type: `MonoBehaviour.CreateCancellationScope()` ties a scope to the component's destroy lifetime (Unity's built-in `destroyCancellationToken`), and `CancellationScope.Run(Func<CancellationToken, UniTask>)` launches fire-and-forget work bound to the scope's token. Awaiting operations directly with `Token` (e.g. `UniTask.WhenAll`) sequences them; because UniTask cancellation throws at the await point, `try/finally` cleanup runs on interruption.

### Changed

- Package now depends on `com.cysharp.unitask` (git URL), adopted as a baseline dependency for async utilities across the toolkit. (UniTask had been dropped in 0.2.0; it returns intentionally.) The `CancellationScope` type stays UniTask-agnostic — the coupling lives only in the extension class.

## [0.2.0] - 2026-05-09

### Added

- `Bun3.Core.Attributes.ReadOnlyAttribute` and matching `ReadOnlyDrawer` — disables a serialized field's inspector editing.
- `UnifiedToggleGroup` — preset-based unified toggle. Editor-time and runtime produce identical results via `[ExecuteAlways]` and a shared invocation path.
- Built-in toggle implementations: `UnifiedToggleCanvasGroup`, `UnifiedToggleGameObject`, `UnifiedToggleImage`, `UnifiedToggleLayoutElement`, `UnifiedToggleToggleGroup` (cascading to another group).
- Extensible options via `UnifiedOption<TComponent, TOption>` with `[SubclassSelector]` inspector UX.
- `Unified Toggle Group` sample.

### Changed

- Package depends on `com.mackysoft.serializereference-extensions` (git URL).
- Migrated from the prototype `com.bun3.unity-ui.unified-toggle-group` repo with UniTask and ZLinq dependencies dropped: `IUnifiedToggle.SetValueAsync(string): UniTask` → `IUnifiedToggle.SetValue(string): void`. All option implementations are synchronous.

## [0.1.0] - 2026-05-08

### Added

- Initial package scaffold.
