using System;
using System.Collections;
using System.Collections.Generic;
using Bun3.Unity.UI.Buttons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Bun3.Unity.UI.Tests
{
    public class ButtonDisabledClickReceiverTests : ButtonScopeTestFixture
    {
        private static void Disable(Button button, SpyHandler handler, string message)
        {
            using var scope = new ButtonInteractableScope(button, handler);
            scope.Require(false, message);
        }

        [Test]
        public void ClickWhileDisabled_ReplaysMessageReason()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            Disable(button, handler, "not enough gold");

            Click(button);

            Assert.AreEqual(1, handler.CallCount);
            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage);
        }

        [Test]
        public void ClickWhileDisabled_DoesNotInvokeOnClick()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            var clicked = 0;
            button.onClick.AddListener(() => clicked++);

            Disable(button, handler, "not enough gold");
            Click(button);

            Assert.AreEqual(0, clicked, "A disabled button's onClick must not fire.");
            Assert.AreEqual(1, handler.CallCount);
        }

        [Test]
        public void ClickWhileDisabled_ReplaysActionReason()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            var invoked = 0;
            Action popup = () => invoked++;

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, popup);
            }

            Click(button);

            Assert.AreEqual(1, handler.CallCount);
            Assert.AreSame(popup, handler.Last.DisabledAction);

            // How to replay is the handler implementation's job; SpyHandler does not invoke it.
            Assert.AreEqual(0, invoked);
        }

        [Test]
        public void RightClickWhileDisabled_DoesNotReplay()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            Disable(button, handler, "not enough gold");

            Click(button, PointerEventData.InputButton.Right);

            Assert.AreEqual(0, handler.CallCount);
        }

        [Test]
        public void AllConditionsMet_NoReceiverIsAdded_AndOnClickFires()
        {
            // The click guard (IsInteractable check) is covered by
            // ClickAfterInteractableRestoredDirectly_DoesNotReplay /
            // ClickWhileParentCanvasGroupBlocksInteraction_... below.
            var button = NewButton();
            var handler = new SpyHandler();
            var clicked = 0;
            button.onClick.AddListener(() => clicked++);

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(true, "never shown");
            }

            Assert.IsFalse(button.TryGetComponent(out ButtonDisabledClickReceiver _),
                "A button that never disables must not get the component.");

            Click(button);

            Assert.AreEqual(0, handler.CallCount);
            Assert.AreEqual(1, clicked);
        }

        [Test]
        public void ClickAfterInteractableRestoredDirectly_DoesNotReplay()
        {
            // Receiver exists (holding a reason) while the button is genuinely interactable —
            // this is the test that catches deleting the OnPointerClick IsInteractable() guard.
            var button = NewButton();
            var handler = new SpyHandler();
            Disable(button, handler, "not enough gold");

            // Restore the raw field directly, bypassing the scope.
            // The receiver still holds the previous reason.
            button.interactable = true;

            Click(button);

            Assert.AreEqual(0, handler.CallCount,
                "When the button is actually interactable, a leftover reason must not replay.");
        }

        [Test]
        public void ClickWhileParentCanvasGroupBlocksInteraction_ReplaysReason_EvenThoughButtonFieldIsTrue()
        {
            // The interactable field is true, but a parent CanvasGroup blocks interaction so
            // IsInteractable() is false. Fails only if `_button.IsInteractable()` were swapped
            // for `_button.interactable`.
            var groupGo = new GameObject("BlockingGroup", typeof(CanvasGroup));
            Track(groupGo);
            groupGo.GetComponent<CanvasGroup>().interactable = false;

            var button = NewButton("GroupedButton");
            var handler = new SpyHandler();
            Disable(button, handler, "not enough gold");
            button.interactable = true; // Restore the raw field to true.

            // SetParent synchronously runs OnTransformParentChanged -> OnCanvasGroupChanged,
            // refreshing Selectable's group-interaction cache (m_GroupsAllowInteraction)
            // immediately — no frame wait needed.
            button.transform.SetParent(groupGo.transform, false);

            Click(button);

            Assert.AreEqual(1, handler.CallCount,
                "IsInteractable() must reflect the parent CanvasGroup's blocking.");
            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage);
        }

        [Test]
        public void RedisablingWithNewReason_ReusesReceiver_AndReplaysLatestReason()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            Disable(button, handler, "reason A");
            Disable(button, handler, "reason B");

            Click(button);

            Assert.AreEqual(1, handler.CallCount);
            Assert.AreEqual("reason B", handler.Last.DisabledMessage,
                "The second disable's reason must overwrite.");
            Assert.AreEqual(1, button.GetComponents<ButtonDisabledClickReceiver>().Length,
                "The receiver must be reused, not added again.");
        }

        [UnityTest]
        public IEnumerator GraphicRaycastOnDisabledButton_DispatchesToReceiver_AndReplaysReason()
        {
            // Core premise of the feature: Selectable.interactable = false does not block
            // raycasts, so another IPointerClickHandler (the receiver) on the same GameObject
            // can receive the click. Other tests dispatch directly via ExecuteEvents.Execute;
            // this one builds a real Canvas + GraphicRaycaster + EventSystem and verifies in one
            // flow that RaycastAll hits the disabled button and that a click dispatched from that
            // hit reaches the receiver and replays the reason.
            var canvasGo = new GameObject(
                "RaycastCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Track(canvasGo);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            // Deliberately no input module: EventSystem.RaycastAll only iterates registered
            // raycasters and does not need one, and this project uses the new Input System —
            // attaching StandaloneInputModule (legacy Input) would throw on UnityEngine.Input
            // access in EventSystem.Update().
            var eventSystemGo = new GameObject("RaycastEventSystem", typeof(EventSystem));
            Track(eventSystemGo);

            var buttonGo = new GameObject(
                "RaycastButton", typeof(RectTransform), typeof(Image), typeof(Button));
            Track(buttonGo);
            buttonGo.transform.SetParent(canvasGo.transform, false);

            var rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var button = buttonGo.GetComponent<Button>();
            var handler = new SpyHandler();
            Disable(button, handler, "raycast reason");

            yield return null;

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) / 2f;
            var screenPoint = RectTransformUtility.WorldToScreenPoint(null, center);

            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = screenPoint,
                button = PointerEventData.InputButton.Left,
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            var hitIndex = results.FindIndex(r => r.gameObject == buttonGo);
            Assert.GreaterOrEqual(hitIndex, 0,
                "GraphicRaycaster must not block the raycast even with Selectable.interactable = false.");

            // Dispatch to the GameObject the raycast returned; using a pre-held reference would
            // disconnect the two halves (hit → dispatch).
            var hit = results[hitIndex];
            pointerData.pointerCurrentRaycast = hit;
            pointerData.pointerPressRaycast = hit;

            ExecuteEvents.Execute(hit.gameObject, pointerData, ExecuteEvents.pointerClickHandler);

            Assert.AreEqual(1, handler.CallCount,
                "A click dispatched from a real raycast hit must reach the receiver.");
            Assert.AreEqual("raycast reason", handler.Last.DisabledMessage);
        }

        [Test]
        public void ReasonedFailure_AddsReceiver()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            Disable(button, handler, "not enough gold");

            Assert.IsTrue(button.TryGetComponent(out ButtonDisabledClickReceiver _));
        }

        [Test]
        public void UnreasonedFailure_DisablesWithoutAddingReceiver()
        {
            var button = NewButton();

            using (var scope = new ButtonInteractableScope(button))
            {
                scope.Require(false);
            }

            Assert.IsFalse(button.interactable);
            Assert.IsFalse(button.TryGetComponent(out ButtonDisabledClickReceiver _));
        }

        [Test]
        public void BecomingInteractableAgain_ClearsStoredReason()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            Disable(button, handler, "not enough gold");

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(true);
            }

            Assert.IsTrue(button.interactable);
            Assert.IsTrue(button.TryGetComponent(out ButtonDisabledClickReceiver _),
                "Once attached, the component is not removed.");

            // Disable the button again, this time without a reason.
            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false);
            }

            Click(button);

            Assert.AreEqual(0, handler.CallCount, "The previous frame's reason must not linger.");
        }

        [Test]
        public void ClickAfterHandlerObjectDestroyed_DoesNotReplay_AndDoesNotThrow()
        {
            // The receiver lives as long as the button, but the handler can die sooner. _handler
            // is an interface type, so `_handler == null` bypasses UnityEngine.Object's
            // overloaded == — a destroyed MonoBehaviour handler passes that check, and without a
            // separate guard Handle() would run on a dead object.
            var handlerGo = new GameObject("MonoHandler", typeof(MonoSpyHandler));
            Track(handlerGo);
            var handler = handlerGo.GetComponent<MonoSpyHandler>();

            var button = NewButton();
            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, "not enough gold");
            }

            UnityEngine.Object.DestroyImmediate(handlerGo);

            Assert.DoesNotThrow(() => Click(button));
            Assert.AreEqual(0, handler.CallCount,
                "A destroyed UnityEngine.Object handler must not receive the reason.");
        }
    }

    /// <summary>
    /// Handler implemented as a <see cref="MonoBehaviour"/> (typical toast-manager shape).
    /// Deliberately touches <c>gameObject</c> so that calling <see cref="Handle"/> after
    /// destruction throws MissingReferenceException on the first Unity API access.
    /// </summary>
    internal sealed class MonoSpyHandler : MonoBehaviour, IButtonDisabledHandler
    {
        public int CallCount { get; private set; }
        public DisabledReason Last { get; private set; }

        public void Handle(DisabledReason reason)
        {
            CallCount++;
            Last = reason;

            _ = gameObject.name;
        }
    }
}
