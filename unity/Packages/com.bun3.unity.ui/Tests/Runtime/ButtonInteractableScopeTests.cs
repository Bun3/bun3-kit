using System;
using Bun3.Unity.UI.Buttons;
using NUnit.Framework;

namespace Bun3.Unity.UI.Tests
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

            Assert.AreEqual(0, handler.CallCount, "The reason must replay only on click, not on Dispose.");
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
                "Declaration order is the priority.");
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
                "A reasonless failure must not occupy the reason slot.");
            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage);
        }

        [Test]
        public void EmptyMessageFailure_DoesNotOccupyReasonSlot()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, string.Empty);
                scope.Require(false, "not enough gold");
            }

            Click(button);

            Assert.IsFalse(button.interactable);
            Assert.AreEqual(1, handler.CallCount);
            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage,
                "An empty string is not a reason; it must not occupy the slot and swallow later conditions.");
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

            Assert.IsTrue(button.interactable, "The second Dispose must do nothing.");
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
