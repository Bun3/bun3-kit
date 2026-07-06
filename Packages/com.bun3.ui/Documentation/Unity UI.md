# About Unity UI

The Unity UI package (`com.bun3.ui`) provides lightweight utilities that simplify common patterns in Unity uGUI. The current focus is making `Button.interactable` state easy to manage when multiple independent conditions affect whether the button can be pressed.

# Installing Unity UI

To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Manual/upm-ui.html).

This package has no additional setup steps and depends only on the built-in `UnityEngine.UI` module.

# Using Unity UI

## ButtonInteractableScope

`ButtonInteractableScope` is a `ref struct` used inside a `using` block. It collects the results of one or more `Require(condition, ...)` calls and applies the AND-combined result to a `Button.interactable` value when the scope is disposed.

```csharp
using Bun3.UI.Buttons;

using var scope = new ButtonInteractableScope(myButton);
scope.Require(condition1, "Reason 1");
scope.Require(condition2, () => HandleReason2());
```

The button's `interactable` is set to `true` only if every `Require` received `true`. As soon as one condition fails, the button will be disabled when the scope closes.

### Handling disabled reasons

When a `Require` call fails, the scope reports a `DisabledReason` (a message and/or an action) to the registered `IButtonDisabledHandler`. After the scope finishes, the handler's `Handle()` is called so it can present the accumulated state — for example by showing a toast, opening a tooltip, or invoking the captured action.

Register a global handler once at application startup:

```csharp
ButtonInteractableScope.DefaultHandler = new MyDisabledHandler();
```

Or supply a handler per scope:

```csharp
using var scope = new ButtonInteractableScope(myButton, new MyDisabledHandler());
```

If neither is set, an internal null handler is used so calls never throw.

### API summary

| Type | Description |
|---|---|
| `ButtonInteractableScope` | `ref struct` that aggregates `Require` results and applies them to `Button.interactable` on dispose. |
| `ButtonInteractableScope.DisabledReason` | Payload describing why a `Require` failed: a `DisabledMessage` string and/or a `DisabledAction` callback. |
| `IButtonDisabledHandler` | Receiver for `DisabledReason` events plus the post-scope `Handle()` callback. |

### Method overloads

| Method | Behavior |
|---|---|
| `Require(bool condition, string disabledMessage = null)` | If `condition` is `false`, reports a `DisabledReason` with the given message (or an empty reason if `null`). |
| `Require(bool condition, Action disabledAction)` | If `condition` is `false` and `disabledAction` is not `null`, reports a `DisabledReason` carrying the action. |

# Technical details

## Requirements

## Package contents

| Location | Description |
|---|---|
| `Runtime/ButtonInteractableScope/` | `ButtonInteractableScope` and `IButtonDisabledHandler` source. |
| `Samples/ButtonInteractableScope/` | Sample MonoBehaviour and handler demonstrating typical usage. |

## Document revision history

| Date | Reason |
|---|---|
| 2026-05-08 | Document created. Matches package version 0.1.0. |
