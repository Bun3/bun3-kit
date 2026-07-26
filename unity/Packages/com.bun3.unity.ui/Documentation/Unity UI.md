# About Unity UI

The Unity UI package (`com.bun3.unity.ui`) provides lightweight utilities that simplify common patterns in Unity uGUI. The current focus is making `Button.interactable` state easy to manage when multiple independent conditions affect whether the button can be pressed.

# Installing Unity UI

To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Manual/upm-ui.html).

This package has no additional setup steps. It uses the built-in `UnityEngine.UI` and `UnityEngine.EventSystems` modules, and its assembly definition references `com.bun3.unity.core` (declared as a package dependency, so the Package Manager resolves it for you).

# Using Unity UI

## ButtonInteractableScope

`ButtonInteractableScope` is a `ref struct` used inside a `using` block. It collects the results of one or more `Require(condition, ...)` calls and applies the AND-combined result to a `Button.interactable` value when the scope is disposed.

```csharp
using Bun3.Unity.UI.Buttons;

using var scope = new ButtonInteractableScope(myButton);
scope.Require(condition1, "Reason 1");
scope.Require(condition2, _handleReason2); // cached Action field
```

The button's `interactable` is set to `true` only if every `Require` received `true`. As soon as one condition fails, the button will be disabled when the scope closes.

The scope is a pure calculator meant to run every frame from `Update()`. It never presents anything itself.

### Reason selection

If several conditions fail in the same scope, the **first failure that carries a reason** wins — declaration order is priority. A failure without a reason is skipped rather than swallowing the ones after it:

```csharp
scope.Require(_isLoaded);                    // fails, no reason -> silently disabled
scope.Require(_gold >= price, "Not enough gold.");  // <- this reason is stored
scope.Require(_hasItem,       "Not enough materials."); // fails, but ignored
```

A `null` or empty `disabledMessage` is not a reason, so it does not occupy the slot either.

### Handling disabled reasons

When the scope closes with a stored `DisabledReason` (a message **or** an action, never both), it hands that reason to a `ButtonDisabledClickReceiver` on the button's GameObject — adding the component if it is not there yet, and only while the application is playing. Seeing this component in the Inspector is expected; it is an implementation detail of the scope and is not meant to be added or edited by hand.

The receiver implements `IPointerClickHandler`. `Selectable.interactable = false` makes `Button.OnPointerClick` a no-op but does not block raycasts, so the receiver still receives the click. When the disabled button is left-clicked, it calls `IButtonDisabledHandler.Handle(reason)` — for example to show a toast, open a tooltip, or invoke the captured action. Deciding between `DisabledMessage` and `DisabledAction` is the handler's responsibility.

Because reasons are replayed on click and not on dispose, re-evaluating the scope every frame costs nothing in presentation.

The receiver's gate is "the button is currently **not** interactable" (via `IsInteractable()`, so a blocking parent `CanvasGroup` counts), not "this scope disabled it". If something else disables the button, the last stored reason is still what gets replayed. This is intended, and it is safe under the package's premise: **one owner decides a given button's `interactable`, every frame.** If two owners fight over the same button, the last `Dispose()` wins and the replayed reason may not match the actual cause.

Register a global handler once at application startup:

```csharp
ButtonInteractableScope.DefaultHandler = new MyDisabledHandler();
```

Or supply a handler per scope:

```csharp
using var scope = new ButtonInteractableScope(myButton, new MyDisabledHandler());
```

If neither is set, an internal null handler is used so calls never throw.

A handler must **outlive the buttons that reference it**, because the receiver holds the reference until the click happens. Prefer an object that lives for the whole application; a short-lived `MonoBehaviour` may already be destroyed by the time the click arrives (the receiver detects a destroyed `UnityEngine.Object` handler and skips it, so the reason is dropped silently). Implementations should also be stateless, since many buttons share one `DefaultHandler`.

### API summary

| Type | Description |
|---|---|
| `ButtonInteractableScope` | `ref struct` that aggregates `Require` results, applies them to `Button.interactable` on dispose, and hands the winning reason to the button's `ButtonDisabledClickReceiver`. |
| `DisabledReason` | Payload describing why a `Require` failed: either a `DisabledMessage` string or a `DisabledAction` callback, never both. `IsEmpty` is `true` when neither is set. |
| `IButtonDisabledHandler` | Single-method strategy — `void Handle(DisabledReason reason)` — invoked when a disabled button is clicked. Only non-empty reasons are passed. |
| `ButtonDisabledClickReceiver` | `MonoBehaviour` attached automatically at runtime. Stores the reason and replays it on left-click while the button is not interactable. |

### Method overloads

| Method | Behavior |
|---|---|
| `Require(bool condition, string disabledMessage = null)` | If `condition` is `false`, stores a `DisabledReason` with the given message. A `null` or empty message stores nothing, so the button is disabled silently. |
| `Require(bool condition, Action disabledAction)` | If `condition` is `false` and `disabledAction` is not `null`, stores a `DisabledReason` carrying the action. |

Either overload only stores a reason if no earlier `Require` in the same scope already claimed the slot.

### Allocation

Arguments are evaluated before `Require` is entered, so the scope cannot prevent allocations at the call site. In a per-frame `Update()`:

- Pass a cached `Action` field, not a method group or lambda — `Require(cond, OpenPopup)` and `Require(cond, () => OpenPopup())` allocate a delegate every frame. Assign `_openPopup = OpenPopup;` once in `Awake()`.
- Prefer constant strings — `Require(gold >= price, $"Need {price - gold} more gold")` allocates a string every frame. Build interpolated messages only when the value changes, and cache them.

# Technical details

## Requirements

- Unity 6000.3
- `UnityEngine.UI` and `UnityEngine.EventSystems` (built-in)
- `com.bun3.unity.core` 0.3.0

## Package contents

| Location | Description |
|---|---|
| `Runtime/Buttons/` | `ButtonInteractableScope`, `DisabledReason`, `IButtonDisabledHandler`, and `ButtonDisabledClickReceiver` source. |
| `Samples/ButtonInteractableScope/` | Sample MonoBehaviour and handler demonstrating typical usage. |
| `Tests/Runtime/` | PlayMode tests (`Bun3.Unity.UI.Tests`). |

## Document revision history

| Date | Reason |
|---|---|
| 2026-07-26 | Reworked for click-time reason replay. Matches package version 0.2.0. |
| 2026-05-08 | Document created. Matches package version 0.1.0. |
