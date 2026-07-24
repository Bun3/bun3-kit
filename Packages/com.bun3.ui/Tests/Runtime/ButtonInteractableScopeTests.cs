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
    }
}
