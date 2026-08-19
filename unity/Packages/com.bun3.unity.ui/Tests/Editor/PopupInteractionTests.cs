using Bun3.Unity.UI.Popups;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupInteractionTests : PopupStackTestFixture
    {
        [Test]
        public void Transition_BlocksRaycasts_UntilOpenCompletes()
        {
            PendingOpen = true;
            Stack.Push("p1");
            var popup = Created[0];
            var group = popup.GetComponent<CanvasGroup>();

            Assert.IsNotNull(group, "A CanvasGroup for transition blocking must be attached.");
            Assert.IsFalse(group.blocksRaycasts, "Raycasts must be blocked during the open transition.");

            popup.OpenSource.TrySetResult();

            Assert.IsTrue(group.blocksRaycasts, "Raycasts must be restored after the open completes.");
        }

        [Test]
        public void Transition_BlocksRaycasts_DuringClose()
        {
            PendingClose = true;
            Stack.Push("p1");
            var popup = Created[0];
            var group = popup.GetComponent<CanvasGroup>();

            Assert.IsTrue(group.blocksRaycasts);

            Stack.Close(popup);

            Assert.IsFalse(group.blocksRaycasts, "Raycasts must be blocked during the close transition.");

            popup.CloseSource.TrySetResult();
        }

        [Test]
        public void DimClick_ClosesPopup_AndRespectsCloseScope()
        {
            WithDim = true;
            WithDimClick = true;
            Stack.Push("p1");
            var popup = Created[0];
            var eventData = new PointerEventData(EventSystem.current);

            using (popup.BlockClose())
            {
                ExecuteEvents.Execute<IPointerClickHandler>(popup.BackgroundDim, eventData,
                    ExecuteEvents.pointerClickHandler);
                Assert.AreEqual(PopupPhase.Open, popup.Phase, "A dim click while locked must only be deferred.");
            }

            Assert.AreEqual(PopupPhase.None, popup.Phase, "The deferred close must run on unlock.");

            Stack.Push("p2");
            var second = Created[1];
            ExecuteEvents.Execute<IPointerClickHandler>(second.BackgroundDim, eventData,
                ExecuteEvents.pointerClickHandler);

            Assert.AreEqual(0, Stack.Count, "The dim click must close the popup.");
        }

        [Test]
        public void TopmostHooks_FireOnCoverAndReveal()
        {
            Stack.Push("p1");
            var first = Created[0];
            Assert.AreEqual(1, first.TopmostGained, "First open also counts as becoming topmost.");

            Stack.Push("p2");
            Assert.AreEqual(1, first.TopmostLost, "Covered notification must fire when one opens above.");
            Assert.AreEqual(1, Created[1].TopmostGained);

            Stack.Pop();
            Assert.AreEqual(2, first.TopmostGained, "Reveal notification must fire when the one above closes.");
        }

        [Test]
        public void CloseAll_Except_KeepsOne()
        {
            Stack.Push("p1");
            Stack.Push("p2");
            Stack.Push("p3");

            Stack.CloseAll(except: Created[1]);

            Assert.AreEqual(1, Stack.Count);
            Assert.AreSame(Created[1], Stack.Top);
        }

        [Test]
        public void CloseAll_Predicate_ClosesMatching()
        {
            Stack.Push("p1", layer: 0);
            Stack.Push("p2", layer: 10);
            Stack.Push("p3", layer: 0);

            Stack.CloseAll(popup => popup.Layer == 0);

            Assert.AreEqual(1, Stack.Count);
            Assert.AreSame(Created[1], Stack.Top);
        }

        [Test]
        public void Emptied_FiresOnlyWhenEverythingGone()
        {
            int emptied = 0;
            Stack.Emptied += () => emptied++;

            Stack.Push("p1");
            var waiting = Stack.WaitUntilEmptyAsync();
            Stack.Enqueue("p2");

            Stack.Pop(); // p1 closes → queued p2 shows, so not empty yet.

            Assert.AreEqual(0, emptied, "Not empty while the queue keeps going.");
            Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending, waiting.Status);

            Stack.Pop(); // p2 closes → truly empty.

            Assert.AreEqual(1, emptied);
            Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded, waiting.Status);
        }

        [Test]
        public void WaitUntilEmptyAsync_CompletesImmediately_WhenAlreadyEmpty()
        {
            Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded,
                Stack.WaitUntilEmptyAsync().Status);
        }
    }
}
