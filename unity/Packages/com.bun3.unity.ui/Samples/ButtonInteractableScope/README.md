# Button Interactable Scope

Demonstrates how to combine multiple conditions into a single `Button.interactable` state with `ButtonInteractableScope`, and how to replay disabled reasons through `IButtonDisabledHandler` when the user clicks a disabled button.

## How it works

1. Register a global handler once during application bootstrap, and cache any `Action` reasons so the per-frame scope does not allocate a delegate every frame:
   ```csharp
   ButtonInteractableScope.DefaultHandler = new MyDisabledHandler();
   _openShopPopup = OpenShopPopup;
   ```
2. Wherever the button state needs to be re-evaluated — typically `Update()` — open a scope and call `Require` for each condition:
   ```csharp
   using var scope = new ButtonInteractableScope(button);
   scope.Require(hasItem, "You don't have the required item.");
   scope.Require(hasGold, _openShopPopup);
   ```
3. When the scope is disposed, `button.interactable` is set to the AND of all `Require` results. If a check failed with a reason, the reason is stored on the button instead of being presented right away. If several failed, the first one that carried a reason wins — declaration order is priority, and a reasonless `Require(false)` does not occupy the slot.
4. When the user clicks the disabled button, `Handle(reason)` runs on the handler. Nothing is shown until then, so a scope re-evaluated every frame never spams the handler.

Storing and catching the click is the job of `ButtonDisabledClickReceiver`, which the scope attaches to the button's GameObject automatically at runtime. Seeing it in the Inspector during Play mode is expected — do not add or edit it by hand.

## Files

- `ButtonInteractableScopeSample.cs` — example MonoBehaviour and a toast-style `IButtonDisabledHandler` implementation.
- `Bun3.Unity.UI.Samples.ButtonInteractableScope.asmdef` — assembly definition for this sample.

## Try it

1. Add the `ButtonInteractableScopeSample` component to a GameObject in a scene.
2. Drag a `UnityEngine.UI.Button` into the `Purchase Button` field.
3. Adjust `Gold` and `Item Count` in the Inspector during Play mode and watch the button toggle.
4. Click the button while it is disabled — the reason is logged only then, once per click. Leaving it disabled without clicking logs nothing.
