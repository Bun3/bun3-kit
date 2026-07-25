# Unity UI

Dev kits for Unity UI. A small collection of utilities that simplify common patterns in Unity uGUI.

## Features

- **`ButtonInteractableScope`** — aggregate multiple conditions into a single `Button.interactable` state, and replay the disabled reason (a message or an action) through `IButtonDisabledHandler` when the user clicks the disabled button.

## Requirements

- `UnityEngine.UI` (built-in)
- [`com.bun3.core`](../com.bun3.core) 0.3.0

## Installation

Install via the Unity Package Manager:

- *Window → Package Manager → Add package from git URL...*
- *URL:* https://github.com/Bun3/unity.git?path=Packages/com.bun3.ui

## Quick Start

```csharp
using Bun3.UI.Buttons;

// Once, at startup.
ButtonInteractableScope.DefaultHandler = new MyDisabledHandler();

// Cached once — never allocate a closure per frame.
private Action _openShopHoursPopup;
private void Awake() => _openShopHoursPopup = OpenShopHoursPopup;

private void Update()
{
    using var scope = new ButtonInteractableScope(_purchaseButton);
    scope.Require(player.HasItem(itemId), "You don't have the required item.");
    scope.Require(player.Gold >= price,   "Not enough gold.");
    scope.Require(IsShopOpen(),           _openShopHoursPopup);
}
```

When the `using` block exits, `_purchaseButton.interactable` is updated to the AND of all `Require` results. Nothing is shown at that moment. If a check failed with a reason, the reason is stored on the button and replayed through the registered `IButtonDisabledHandler` **when the user clicks the disabled button** — so a scope re-evaluated every frame does not spam the handler.

If several conditions fail together, the **first failure that carries a reason** wins; declaration order is priority. A reasonless `Require(false)` disables the button without occupying the reason slot, so a later reasoned failure is still adopted.

> A `ButtonDisabledClickReceiver` component is attached to the button's GameObject automatically at runtime to store the reason and catch the click. Seeing it in the Inspector is expected — do not add or edit it by hand.

A complete example is included as a Package Manager sample (`Button Interactable Scope`). See `Samples/ButtonInteractableScope/` after import.

## Links

- [Documentation](Documentation/Unity%20UI.md)
- [Changelog](CHANGELOG.md)
- [Third Party Notices](Third%20Party%20Notices.md)
