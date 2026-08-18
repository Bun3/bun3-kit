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

## Popup stack

`Bun3.Unity.UI.Popups` provides a domain-agnostic popup/modal stack: push/pop with layer ordering, duplicate policies, a sequential display queue, back-key routing, and animation await points. Everything game-specific — prefab loading, parenting, dim/sound presentation — is injected through delegates and virtual methods.

```csharp
using Bun3.Unity.UI.Popups;

// 1. Derive popups from Popup (override animation/back hooks as needed).
public sealed class ShopPopup : Popup
{
    protected override UniTask PlayOpenAsync(CancellationToken ct) => _tween.PlayAsync(ct);
    protected override bool OnBackRequested() => !_isPurchasing; // refuse close mid-purchase
}

// 2. Create one stack, supplying how popups are created and released.
_stack = new PopupStack(
    factory: (key, ct) => LoadAndInstantiateAsync(key, ct), // Resources/Addressables/pool — your call
    releaser: popup => Destroy(popup.gameObject));           // omit for default Destroy

// 3. Drive it.
_stack.Push((int)PopupId.Shop);                       // fire-and-forget
var popup = await _stack.PushAsync((int)PopupId.Shop); // await open animation, get the instance
_stack.Push((int)PopupId.Shop, layer: 10);            // higher layer stays on top
_stack.Push((int)PopupId.Shop, duplicate: PopupDuplicatePolicy.Replace);
_stack.Enqueue((int)PopupId.Reward);                  // shows when the stack is empty, one at a time
_stack.HandleBack();                                  // route ESC/Android back to the top popup

// 4. Pass initial data without synchronous instantiation: implement IPopupArg<TArg>
//    and it arrives right after the async load, before the open animation.
_stack.PushWithArg((int)PopupId.ItemDetail, new ItemDetailArgs(itemId));
_stack.EnqueueWithArg((int)PopupId.Reward, rewardArgs);

// A popup may implement IPopupArg<TArg> for several TArg types to expose multiple
// initialization routes — e.g. a typed code path plus a designer-data string path.
// The static type at the call site picks which OnPopupArg runs.
public sealed class ItemDetailPopup : Popup, IPopupArg<int>, IPopupArg<string>
{
    public void OnPopupArg(int defId)     { /* from code */ }
    public void OnPopupArg(string token)  => OnPopupArg(int.Parse(token)); // from table/server data
}
// To make the string route mandatory for every popup (like an abstract token method),
// enforce it in your game's base class: abstract class GamePopup : Popup, IPopupArg<string>.

// 5. Reuse an already-open instance instead of stacking a duplicate (legacy GetOrShowPopup):
//    moves it to the top of its layer and re-delivers the arg via IPopupArg.
var shop = await _stack.PushWithArgAsync((int)PopupId.Shop, shopArgs,
    duplicate: PopupDuplicatePolicy.Focus);

// 6. Channel queue: show one at a time *within this queue* — on top of other popups.
//    (PopupStack.Enqueue waits for an empty stack; PopupQueue only waits for its own popup.)
//    Higher priority shows first; FIFO within the same priority.
_rewardQueue = new PopupQueue(_stack);
_rewardQueue.EnqueueWithArg((int)PopupId.Promotion, rankArgs, priority: 2);
_rewardQueue.EnqueueWithArg((int)PopupId.GotItems, itemArgs);   // priority 0

// 7. Pooling + preload: plug the pool straight into the stack.
_pool = new PopupPool(LoadPopupAsync);
_stack = new PopupStack(_pool.RentAsync, _pool.Return);
await _pool.PreloadAsync((int)PopupId.Shop);   // marks the key pooled + stocks an instance

// 8. Sibling ordering: optional arranger keeps sibling indices matching stack order
//    (assumes a popup-only parent). Order notifications and dim handling live in the
//    stack itself, so this is purely about transform order.
_arranger = new PopupSiblingArranger(_stack);

// 8b. Dim: assign the popup prefab's dim object to the serialized BackgroundDim field
//     (leave null for dimless popups). The stack keeps exactly one dim visible — the
//     topmost popup that HAS a dim — so a dimless popup on top keeps the dim below it.

// 8c. Or wire everything at once and let Dispose() tear it down in order:
_popups = new PopupManagerBuilder(LoadPopupAsync)
    .UsePool()                                  // wraps the loader in a PopupPool
    .UseBackKey(gameObject, ShowQuitDialog)     // attaches PopupBackKeyRouter, injects the stack
    .UseSiblingArranger()
    .Build();
_popups.Stack.Push((int)PopupId.Shop);

// 8d. Global access (legacy GameManager.Get().ShowPopup style): assign the built manager
//     to the optional static slot in your bootstrap. Dispose() clears it automatically.
//     The manager mirrors the common stack verbs, so no .Stack hop is needed day to day.
PopupManager.Instance = _popups;
PopupManager.Instance.Push((int)PopupId.Shop);                    // from anywhere
PopupManager.Instance.PushWithArg((int)PopupId.ItemDetail, args);
PopupManager.Instance.Enqueue((int)PopupId.Reward);

// 8e. Result popups (legacy Callback(int result)): derive from Popup<TResult>, call
//     SetResult before closing. Closing without SetResult (back key, cancel) yields
//     defaultResult — cancel needs no extra code.
public sealed class ConfirmPopup : Popup<bool>, IPopupArg<string>
{
    public void OnPopupArg(string message) { /* bind label */ }
    void OnYes() { SetResult(true); Close(); }
    void OnNo()  => Close();
}
// Prefer typed keys: declare the key↔result contract once, and every call site is
// inferred AND compile-checked (a raw int key can only be checked at runtime).
public static class PopupIds
{
    public static readonly PopupKey<bool>         Confirm  = new((int)PopupId.Confirm);
    public static readonly PopupKey<ItemInstance> ItemPick = new((int)PopupId.ItemPick);
}
bool ok     = await _stack.PushForResultAsync(PopupIds.Confirm, "Delete this?");
var picked  = await _stack.PushForResultAsync(PopupIds.ItemPick);   // null = cancelled
_stack.Push(PopupIds.Confirm);   // implicit conversion — same key works on every API

// 9. Block closing while the popup is busy (ref-counted; nested locks compose).
//    A Close/back during the lock is *deferred*, not lost — it runs when the last lock lifts.
using (BlockClose())                                   // sequence direction, cutscenes
    await PlaySequenceAsync(ct);
var res = await BlockCloseWhile(SendPacketAsync(req, ct)); // server round-trip
```

Behavior rules:

- The stack is ordered by (layer ascending, push order); `Top` is the end. Back-key routing, `Pop()`, and `HandleBack()` always target the top.
- `PopupDuplicatePolicy` decides what happens when the same `PopupKey` is already open or loading: `Ignore` (default), `Queue` (append to the sequential queue), or `Replace` (close the existing instance, open a new one).
- `Enqueue` items display one at a time, each waiting until the stack is completely empty — the pattern for reward chains. `popup.WaitUntilClosedAsync()` awaits an individual popup's dismissal.
- `HandleBack()` consumes the key whenever any popup is present. A top popup in transition swallows the input; `OnBackRequested()` returning `false` refuses the close. Only an empty stack returns `false`, letting the game show its own quit dialog. Attach the optional `PopupBackKeyRouter` component (assign its `Stack`) to poll ESC/Android back automatically under both input backends.
- `Clear()` skips animations, cancels in-flight loads, ignores close locks, and releases everything — for scene transitions.
- While `IsCloseBlocked` (any `BlockClose`/`BlockCloseWhile` lock held), back keys are consumed without routing and `Close` requests are deferred until the last lock releases. Hook `OnCloseBlockedChanged(bool)` to drive raycast blocking or spinners.
- The push/pop/back paths allocate no closures, LINQ, or strings. (`*WithArg` uses generics — no boxing on the direct path; only queued args allocate a small holder, and that queue is a cold path.)

# Technical details

## Requirements

- Unity 6000.3
- `UnityEngine.UI` and `UnityEngine.EventSystems` (built-in)
- `com.bun3.unity.core` 0.3.0
- `com.cysharp.unitask` (popup lifecycle awaits)

## Package contents

| Location | Description |
|---|---|
| `Runtime/Buttons/` | `ButtonInteractableScope`, `DisabledReason`, `IButtonDisabledHandler`, and `ButtonDisabledClickReceiver` source. |
| `Runtime/Popups/` | `PopupStack`, `Popup`, `Popup<TResult>`, `PopupKey`, `PopupDuplicatePolicy`, `IPopupArg<TArg>`, `PopupCloseGuard`, `PopupQueue`, `PopupPool`, `PopupSiblingArranger`, `PopupBackKeyRouter`, `PopupManager`(+builder) source. |
| `Samples/ButtonInteractableScope/` | Sample MonoBehaviour and handler demonstrating typical usage. |
| `Tests/Runtime/` | PlayMode tests (`Bun3.Unity.UI.Tests`). |
| `Tests/Editor/` | EditMode tests (`Bun3.Unity.UI.Editor.Tests`), covering the popup stack. |

## Document revision history

| Date | Reason |
|---|---|
| 2026-08-17 | Added the popup/modal stack (`Bun3.Unity.UI.Popups`). |
| 2026-07-26 | Reworked for click-time reason replay. Matches package version 0.2.0. |
| 2026-05-08 | Document created. Matches package version 0.1.0. |
