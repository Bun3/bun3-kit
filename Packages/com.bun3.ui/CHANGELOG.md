# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-07-26

### Changed

- **Breaking.** Disabled reasons are now replayed when the user clicks the disabled button, not when the scope is disposed. A scope opened in `Update()` no longer fires the handler every frame.
- **Breaking.** `IButtonDisabledHandler` is reduced to a single member, `void Handle(DisabledReason reason)`. The previous two-method shape (`OnDisabled(...)` collection plus a no-arg `Handle()` replay) forced implementations to hold the last reason in a field, which cross-contaminated buttons sharing one `DefaultHandler`.
- **Breaking.** `DisabledReason` is promoted from a type nested in `ButtonInteractableScope` to the top-level `Bun3.UI.Buttons.DisabledReason`.
- When several conditions fail together, the **first failure that carries a reason** wins. A reasonless `Require(false)` disables the button without occupying the reason slot, so a later reasoned failure is still adopted.
- Source moved from `Runtime/ButtonInteractableScope/` to `Runtime/Buttons/`.
- The package now declares its dependency on `com.bun3.core`, which `bun3.ui.asmdef` already referenced.

### Added

- `ButtonDisabledClickReceiver` — a `MonoBehaviour` attached automatically to the button's GameObject at runtime when a reasoned failure occurs. It stores the winning reason and replays it through the handler on left-click while the button is not interactable. Seeing it in the Inspector is expected; it is not meant to be added or edited by hand.
- `DisabledReason` — top-level `readonly struct` carrying either a `DisabledMessage` or a `DisabledAction`, never both.
- PlayMode test assembly `Bun3.UI.Tests` covering reason selection, the click gate, receiver lifetime, and the raycast-to-dispatch path.

### Fixed

- An empty-string `disabledMessage` no longer counts as a reason. Previously `Require(false, "")` both replayed an empty message and suppressed every later reason.
- A destroyed `UnityEngine.Object` handler is no longer invoked on click. The stored handler is typed as the interface, so a plain null check missed Unity's overloaded equality operator and `Handle` ran on a dead object.
- `ButtonInteractableScope.DefaultHandler` resets on `SubsystemRegistration`, so a handler assigned in a previous play session no longer survives when Enter Play Mode Options skip the domain reload.

### Removed

- `IButtonDisabledHandler.OnDisabled(...)` and the no-arg `IButtonDisabledHandler.Handle()`.
- The nested `ButtonInteractableScope.DisabledReason` type.
- Reason replay from `ButtonInteractableScope.Dispose()`.

## [0.1.0] - 2026-05-08

### Added

- Initial release.
- `ButtonInteractableScope` (`ref struct`) — combines multiple `Require` checks into a single `Button.interactable` state, applied on dispose.
- `IButtonDisabledHandler` — receives `DisabledReason` events while the scope is open, and a final `Handle()` callback when the scope completes.
- Default no-op handler so consumers do not have to register one before use.
- `Button Interactable Scope` sample demonstrating typical usage with a toast-style handler.
