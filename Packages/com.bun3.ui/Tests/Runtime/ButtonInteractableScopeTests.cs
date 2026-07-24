using System;
using Bun3.UI.Buttons;
using NUnit.Framework;

namespace Bun3.UI.Tests
{
    public class ButtonInteractableScopeTests : ButtonScopeTestFixture
    {
        [Test]
        public void AllConditionsMet_ButtonStaysInteractable()
        {
            var button = NewButton();
            button.interactable = false;

            using (var scope = new ButtonInteractableScope(button))
            {
                scope.Require(true, "never shown");
                scope.Require(true);
            }

            Assert.IsTrue(button.interactable);
        }

        [Test]
        public void AnyFailedCondition_DisablesButton()
        {
            var button = NewButton();

            using (var scope = new ButtonInteractableScope(button))
            {
                scope.Require(true);
                scope.Require(false, "not enough gold");
                scope.Require(true);
            }

            Assert.IsFalse(button.interactable);
        }

        [Test]
        public void Dispose_DoesNotInvokeHandler()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, "not enough gold");
            }

            Assert.AreEqual(0, handler.CallCount, "사유는 Dispose가 아니라 클릭 시점에만 재생돼야 한다.");
        }

        [Test]
        public void DefaultHandler_NullAssignment_FallsBackToNoOp()
        {
            ButtonInteractableScope.DefaultHandler = null;

            Assert.IsNotNull(ButtonInteractableScope.DefaultHandler);

            var button = NewButton();
            using (var scope = new ButtonInteractableScope(button))
            {
                scope.Require(false, "not enough gold");
            }

            Assert.IsFalse(button.interactable);
        }

        [Test]
        public void MultipleReasonedFailures_FirstOneWins()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, "not enough gold");
                scope.Require(false, "not enough materials");
            }

            Click(button);

            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage,
                "선언 순서가 우선순위다.");
        }

        [Test]
        public void UnreasonedFailureFirst_LaterReasonStillWins()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false);
                scope.Require(false, "not enough gold");
                scope.Require(false, "not enough materials");
            }

            Click(button);

            Assert.AreEqual(1, handler.CallCount,
                "사유 없는 실패는 사유 슬롯을 점유하지 않는다.");
            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage);
        }

        [Test]
        public void MessageReasonBeforeActionReason_MessageWins()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            Action popup = () => { };

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, "not enough gold");
                scope.Require(false, popup);
            }

            Click(button);

            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage);
            Assert.IsNull(handler.Last.DisabledAction);
        }

        [Test]
        public void DoubleDispose_IsIdempotent()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            var scope = new ButtonInteractableScope(button, handler);
            scope.Require(false, "not enough gold");
            scope.Dispose();

            button.interactable = true;
            scope.Dispose();

            Assert.IsTrue(button.interactable, "두 번째 Dispose는 아무 것도 하지 않아야 한다.");
        }

        [Test]
        public void TwoScopesOnSameButton_LastDisposeWins()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            using (var first = new ButtonInteractableScope(button, handler))
            {
                first.Require(false, "from first scope");
            }

            using (var second = new ButtonInteractableScope(button, handler))
            {
                second.Require(false, "from second scope");
            }

            Click(button);

            Assert.AreEqual(1, handler.CallCount);
            Assert.AreEqual("from second scope", handler.Last.DisabledMessage);
        }

        [Test]
        public void DestroyedButton_DisposeDoesNotThrow()
        {
            var button = NewButton();
            UnityEngine.Object.DestroyImmediate(button.gameObject);

            Assert.DoesNotThrow(() =>
            {
                using var scope = new ButtonInteractableScope(button);
                scope.Require(false, "not enough gold");
            });
        }

        [Test]
        public void NullButton_DisposeDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                using var scope = new ButtonInteractableScope(null);
                scope.Require(false, "not enough gold");
            });
        }

        [Test]
        public void TwoButtonsSharingDefaultHandler_DoNotCrossContaminate()
        {
            ButtonInteractableScope.DefaultHandler = new SpyHandler();
            var shared = (SpyHandler)ButtonInteractableScope.DefaultHandler;

            var gold = NewButton("GoldButton");
            var level = NewButton("LevelButton");

            using (var scope = new ButtonInteractableScope(gold))
            {
                scope.Require(false, "not enough gold");
            }

            using (var scope = new ButtonInteractableScope(level))
            {
                scope.Require(false, "level too low");
            }

            Click(gold);
            Assert.AreEqual("not enough gold", shared.Last.DisabledMessage);

            Click(level);
            Assert.AreEqual("level too low", shared.Last.DisabledMessage);

            Assert.AreEqual(2, shared.CallCount);
        }
    }
}
