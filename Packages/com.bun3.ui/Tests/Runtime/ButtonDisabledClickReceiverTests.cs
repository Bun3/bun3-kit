using System;
using Bun3.UI.Buttons;
using NUnit.Framework;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.UI.Tests
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

            Assert.AreEqual(0, clicked, "비활성 버튼의 onClick은 발화하면 안 된다.");
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

            // 재생 방식은 핸들러 구현의 책임이다. SpyHandler는 실행하지 않는다.
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
        public void ClickWhileInteractable_DoesNotReplay_AndInvokesOnClick()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            var clicked = 0;
            button.onClick.AddListener(() => clicked++);

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(true, "never shown");
            }

            Click(button);

            Assert.AreEqual(0, handler.CallCount);
            Assert.AreEqual(1, clicked);
        }

        [Test]
        public void AllConditionsMet_NoReceiverIsAdded()
        {
            var button = NewButton();

            using (var scope = new ButtonInteractableScope(button))
            {
                scope.Require(true, "never shown");
            }

            Assert.IsFalse(button.TryGetComponent(out ButtonDisabledClickReceiver _),
                "비활성화될 일이 없는 버튼에는 컴포넌트가 붙지 않아야 한다.");
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
                "한 번 붙은 컴포넌트는 제거하지 않는다.");

            // 버튼을 다시 비활성화하되 사유는 주지 않는다.
            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false);
            }

            Click(button);

            Assert.AreEqual(0, handler.CallCount, "이전 프레임의 사유가 남아 있으면 안 된다.");
        }
    }
}
