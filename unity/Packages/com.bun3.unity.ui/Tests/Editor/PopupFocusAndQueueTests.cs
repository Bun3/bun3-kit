using System.Text.RegularExpressions;
using Bun3.Unity.UI.Popups;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupFocusAndQueueTests : PopupStackTestFixture
    {
        [Test]
        public void Focus_ReusesExistingInstance_AndMovesToTop()
        {
            Stack.Push("p1");
            Stack.Push("p2");
            Popup focused = null;
            Stack.Focused += popup => focused = popup;

            var result = Stack.PushAsync("p1", duplicate: PopupDuplicatePolicy.Focus)
                .GetAwaiter().GetResult();

            Assert.AreSame(Created[0], result, "Must return the existing instance, not create a new one.");
            Assert.AreEqual(2, Created.Count);
            Assert.AreSame(Created[0], Stack.Top, "The existing instance must move to the top.");
            Assert.AreSame(Created[0], focused);
        }

        [Test]
        public void Focus_WithArg_RedeliversToExisting()
        {
            Stack.PushWithArg("p1", arg: 10);

            Stack.PushWithArgAsync("p1", arg: 20, duplicate: PopupDuplicatePolicy.Focus)
                .GetAwaiter().GetResult();

            Assert.AreEqual(1, Created.Count);
            Assert.AreEqual(20, Created[0].ReceivedArg, "The arg must be re-delivered to the existing instance.");
        }

        [Test]
        public void Focus_RespectsLayer_StaysBelowHigherLayer()
        {
            Stack.Push("p1", layer: 0);
            Stack.Push("p2", layer: 10);

            Stack.PushAsync("p1", duplicate: PopupDuplicatePolicy.Focus).GetAwaiter().GetResult();

            Assert.AreSame(Created[1], Stack.Top, "Focus raises only within its own layer.");
        }

        [Test]
        public void PopupsView_MatchesStackOrder()
        {
            Stack.Push("p1", layer: 0);
            Stack.Push("p2", layer: 10);
            Stack.Push("p3", layer: 0);

            var view = Stack.Popups;

            Assert.AreEqual(3, view.Count);
            Assert.AreSame(Created[0], view[0]);
            Assert.AreSame(Created[2], view[1]);
            Assert.AreSame(Created[1], view[2]);
        }

        [Test]
        public void PopupQueue_ShowsOnTopOfOtherPopups_OneAtATime()
        {
            Stack.Push("p1"); // Like a mailbox — stays open.
            var queue = new PopupQueue(Stack);

            queue.Enqueue("p2");
            queue.Enqueue("p3");

            // Unlike the stack queue, it shows immediately even with another popup open.
            Assert.AreEqual(2, Stack.Count);
            Assert.AreSame(Created[1], Stack.Top);
            Assert.AreEqual(1, queue.Count);
            Assert.AreSame(Created[1], queue.Current);

            Stack.Close(Created[1]);

            Assert.AreSame(Created[2], Stack.Top, "The next must show when this queue's popup closes.");
            Assert.AreEqual(0, queue.Count);

            Stack.Close(Created[2]);

            Assert.AreEqual(1, Stack.Count, "The popup underneath stays put.");
            Assert.IsNull(queue.Current);
        }

        [Test]
        public void PopupQueue_HigherPriorityShowsFirst_FifoWithinSame()
        {
            Stack.Push("p1"); // Background popup that does not block queue display.
            var queue = new PopupQueue(Stack);
            Stack.Close(Created[0]);

            // Nothing is showing, so the first Enqueue opens immediately; the rest verify ordering.
            queue.Enqueue("p2", priority: 0);          // Shows immediately.
            queue.Enqueue("p3", priority: 0);          // Normal.
            queue.Enqueue("p4", priority: 2);          // Like a promotion.
            queue.Enqueue("p5", priority: 1);          // Like a special item.

            Stack.Close(Created[1]);
            Assert.AreEqual("p4", Stack.Top.Key.Name, "Higher-priority entries must show first.");

            Stack.Pop();
            Assert.AreEqual("p5", Stack.Top.Key.Name);

            Stack.Pop();
            Assert.AreEqual("p3", Stack.Top.Key.Name, "Equal priorities must keep insertion order.");
        }

        [Test]
        public void PopupQueue_WithArg_Delivers()
        {
            var queue = new PopupQueue(Stack);

            queue.EnqueueWithArg("p1", arg: 77);

            Assert.AreEqual(77, Created[0].ReceivedArg);
        }

        [Test]
        public void PopupQueue_EntryThrow_ContinuesToNext()
        {
            LogAssert.Expect(LogType.Exception, new Regex("queue load fail"));

            var stack = new PopupStack(
                (key, ct) => key.Name == "p1"
                    ? throw new System.InvalidOperationException("queue load fail")
                    : CreatePopup(key, ct),
                ReleasePopup);
            var queue = new PopupQueue(stack);

            queue.Enqueue("p1"); // Key whose load throws — must log and continue.
            queue.Enqueue("p2");

            Assert.AreEqual(1, stack.Count, "A failed entry must not stall the queue.");
            Assert.AreEqual("p2", stack.Top.Key.Name);

            stack.Dispose();
        }

        [Test]
        public void PopupQueue_Clear_DropsPendingOnly()
        {
            var queue = new PopupQueue(Stack);
            queue.Enqueue("p1");
            queue.Enqueue("p2");

            queue.Clear();

            Assert.AreEqual(1, Stack.Count, "The showing popup must be kept.");
            Assert.AreEqual(0, queue.Count);

            Stack.Pop();
            Assert.AreEqual(0, Stack.Count, "Dropped entries must not show.");
        }
    }
}
