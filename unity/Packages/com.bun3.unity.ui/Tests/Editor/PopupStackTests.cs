using System.Threading;
using Bun3.Unity.UI.Popups;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupStackTests : PopupStackTestFixture
    {
        [Test]
        public void Push_OpensPopup_TopAndPhaseSet()
        {
            Stack.Push("p1");

            Assert.AreEqual(1, Stack.Count);
            Assert.AreSame(Created[0], Stack.Top);
            Assert.AreEqual(PopupPhase.Open, Created[0].Phase);
            Assert.AreEqual(new PopupKey("p1"), Created[0].Key);
            Assert.IsTrue(Stack.IsOpen("p1"));
        }

        [Test]
        public void Push_HigherLayerStaysOnTop_OfLaterLowerLayerPush()
        {
            Stack.Push("p1", layer: 0);
            Stack.Push("p2", layer: 10);
            Stack.Push("p3", layer: 0);

            // Sort: (layer ascending, insertion order) — layer 10 always stays on top.
            Assert.AreSame(Created[1], Stack.Top);
            Assert.AreEqual(3, Stack.Count);
        }

        [Test]
        public void Push_SameLayer_LaterPushIsTop()
        {
            Stack.Push("p1");
            Stack.Push("p2");

            Assert.AreSame(Created[1], Stack.Top);
        }

        [Test]
        public void Push_DuplicateIgnore_SecondRequestDropped()
        {
            Stack.Push("p1");
            Stack.Push("p1");

            Assert.AreEqual(1, Stack.Count);
            Assert.AreEqual(1, Created.Count);
        }

        [Test]
        public void Push_DuplicateReplace_ClosesExistingAndOpensNew()
        {
            Stack.Push("p1");
            var first = Created[0];

            Stack.Push("p1", duplicate: PopupDuplicatePolicy.Replace);

            Assert.AreEqual(1, Stack.Count);
            Assert.AreSame(Created[1], Stack.Top);
            Assert.AreEqual(PopupPhase.None, first.Phase);
            CollectionAssert.Contains(Released, first);
        }

        [Test]
        public void Push_DuplicateQueue_WaitsUntilExistingCloses()
        {
            Stack.Push("p1");
            Stack.Push("p1", duplicate: PopupDuplicatePolicy.Queue);

            Assert.AreEqual(1, Stack.Count);
            Assert.AreEqual(1, Stack.QueuedCount);

            Stack.Pop();

            Assert.AreEqual(1, Stack.Count, "Queued same-key entry must open once the existing popup closes.");
            Assert.AreSame(Created[1], Stack.Top);
            Assert.AreEqual(0, Stack.QueuedCount);
        }

        [Test]
        public void HandleBack_EmptyStack_NotConsumed()
        {
            Assert.IsFalse(Stack.HandleBack());
        }

        [Test]
        public void HandleBack_ClosesTopOnly()
        {
            Stack.Push("p1");
            Stack.Push("p2");

            Assert.IsTrue(Stack.HandleBack());

            Assert.AreEqual(1, Stack.Count);
            Assert.AreSame(Created[0], Stack.Top);
            Assert.AreEqual(1, Created[1].BackRequests);
            Assert.AreEqual(0, Created[0].BackRequests, "Back must route only to the topmost popup.");
        }

        [Test]
        public void HandleBack_Rejected_ConsumedButNotClosed()
        {
            Stack.Push("p1");
            Created[0].RejectBack = true;

            Assert.IsTrue(Stack.HandleBack());

            Assert.AreEqual(1, Stack.Count);
            Assert.AreEqual(PopupPhase.Open, Created[0].Phase);
        }

        [Test]
        public void HandleBack_DuringOpening_ConsumedWithoutRouting()
        {
            PendingOpen = true;
            Stack.Push("p1");

            Assert.AreEqual(PopupPhase.Opening, Created[0].Phase);
            Assert.IsTrue(Stack.HandleBack(), "The key must be consumed even during a transition.");
            Assert.AreEqual(0, Created[0].BackRequests);
            Assert.AreEqual(1, Stack.Count);
        }

        [Test]
        public void Enqueue_DrainsSequentially()
        {
            Stack.Enqueue("p1");
            Stack.Enqueue("p2");

            Assert.AreEqual(1, Stack.Count, "The head entry must show immediately.");
            Assert.AreEqual(1, Stack.QueuedCount);

            Stack.Pop();

            Assert.AreEqual(1, Stack.Count);
            Assert.AreSame(Created[1], Stack.Top);

            Stack.Pop();

            Assert.AreEqual(0, Stack.Count);
            Assert.AreEqual(0, Stack.QueuedCount);
        }

        [Test]
        public void Enqueue_WhileStackOccupied_WaitsForEmpty()
        {
            Stack.Push("p1");
            Stack.Enqueue("p2");

            Assert.AreEqual(1, Stack.Count);
            Assert.AreEqual(1, Stack.QueuedCount);

            Stack.Pop();

            Assert.AreSame(Created[1], Stack.Top);
            Assert.AreEqual(0, Stack.QueuedCount);
        }

        [Test]
        public void Close_DuringOpening_DeferredUntilOpenCompletes()
        {
            PendingOpen = true;
            Stack.Push("p1");
            var popup = Created[0];

            Stack.Close(popup);

            Assert.AreEqual(PopupPhase.Opening, popup.Phase, "Close must only be deferred during the open transition.");
            Assert.AreEqual(1, Stack.Count);

            popup.OpenSource.TrySetResult();

            Assert.AreEqual(PopupPhase.None, popup.Phase);
            Assert.AreEqual(0, Stack.Count);
            CollectionAssert.Contains(Released, popup);
        }

        [Test]
        public void Close_DuringClosing_SecondRequestIgnored()
        {
            PendingClose = true;
            Stack.Push("p1");
            var popup = Created[0];

            Stack.Close(popup);
            Stack.Close(popup);

            Assert.AreEqual(PopupPhase.Closing, popup.Phase);

            popup.CloseSource.TrySetResult();

            Assert.AreEqual(0, Stack.Count);
            Assert.AreEqual(1, Released.Count, "The releaser must be called only once.");
        }

        [Test]
        public void WaitUntilClosedAsync_CompletesOnClose()
        {
            Stack.Push("p1");
            var popup = Created[0];
            var waiting = popup.WaitUntilClosedAsync();

            Assert.AreEqual(UniTaskStatus.Pending, waiting.Status);

            Stack.Close(popup);

            Assert.AreEqual(UniTaskStatus.Succeeded, waiting.Status);
        }

        [Test]
        public void Clear_ReleasesEverythingImmediately()
        {
            PendingClose = true;
            Stack.Push("p1");
            Stack.Push("p2");
            Stack.Enqueue("p3");
            Stack.Close(Created[1]); // Put it in the closing-transition state.

            Stack.Clear();

            Assert.AreEqual(0, Stack.Count);
            Assert.AreEqual(0, Stack.QueuedCount);
            Assert.AreEqual(2, Released.Count, "Everything must be released without waiting for transitions.");
            Assert.AreEqual(PopupPhase.None, Created[0].Phase);
            Assert.AreEqual(PopupPhase.None, Created[1].Phase);
        }

        [Test]
        public void Clear_DuringOpening_PushAsyncReturnsNull()
        {
            PendingOpen = true;
            var pushTask = Stack.PushAsync("p1");

            Stack.Clear();
            Created[0].OpenSource.TrySetResult();

            Assert.IsNull(pushTask.GetAwaiter().GetResult(),
                "Clear during the open transition must return null, not an already-released instance.");
        }

        [Test]
        public void Enqueue_FactoryThrow_ContinuesDraining()
        {
            // A subscriber replaces UniTask's default exception logging — keeps the test log clean.
            static void Swallow(System.Exception _) { }
            UniTaskScheduler.UnobservedTaskException += Swallow;
            try
            {
                var stack = new PopupStack(
                    (key, ct) => key.Name == "p1"
                        ? throw new System.InvalidOperationException("load fail")
                        : CreatePopup(key, ct),
                    ReleasePopup);

                stack.Push("p9");
                stack.Enqueue("p1"); // Key whose load throws.
                stack.Enqueue("p2");

                stack.Pop(); // Stack empties, draining starts → p1 fails, must continue to p2.

                Assert.AreEqual(1, stack.Count, "The entry after the failed one must show.");
                Assert.AreEqual("p2", stack.Top.Key.Name);

                stack.Dispose();
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= Swallow;
            }
        }

        [Test]
        public void Clear_DuringFactoryLoad_ReleasesLateArrival()
        {
            var source = new UniTaskCompletionSource<Popup>();
            var released = 0;
            var stack = new PopupStack(
                (key, ct) => source.Task,
                popup => released++);

            stack.Push("p1");
            stack.Clear();

            var late = new UnityEngine.GameObject("late").AddComponent<TestPopup>();
            source.TrySetResult(late);

            Assert.AreEqual(0, stack.Count, "An instance arriving after cancellation must not enter the stack.");
            Assert.AreEqual(1, released);

            stack.Dispose();
            UnityEngine.Object.DestroyImmediate(late.gameObject);
        }

        [Test]
        public void AsyncFactory_BlocksDuplicateAndQueue_UntilLoaded()
        {
            var source = new UniTaskCompletionSource<Popup>();
            Popup created = null;
            var stack = new PopupStack((key, ct) => source.Task, ReleasePopup);

            stack.Push("p1");
            stack.Push("p1"); // Duplicate while loading — Ignore.
            stack.Enqueue("p2");

            Assert.AreEqual(0, stack.Count);
            Assert.AreEqual(1, stack.QueuedCount, "The queue must not drain while loading.");

            created = new UnityEngine.GameObject("loaded").AddComponent<TestPopup>();
            Created.Add((TestPopup)created);
            source.TrySetResult(created);

            Assert.AreEqual(1, stack.Count, "A duplicate Push while loading must be ignored.");
            Assert.AreSame(created, stack.Top);

            stack.Dispose();
        }

        [Test]
        public void FactoryReturningNull_LeavesStackUsable()
        {
            var stack = new PopupStack((key, ct) => UniTask.FromResult<Popup>(null), ReleasePopup);

            stack.Push("p1");

            Assert.AreEqual(0, stack.Count);
            Assert.IsFalse(stack.HandleBack());

            stack.Dispose();
        }

        [Test]
        public void Dispose_ThenPush_Throws()
        {
            Stack.Dispose();

            Assert.Throws<System.ObjectDisposedException>(() => Stack.Push("p1"));
        }

        [Test]
        public void Events_RaisedOnOpenAndClose()
        {
            Popup opened = null;
            Popup closed = null;
            Stack.Opened += popup => opened = popup;
            Stack.Closed += popup => closed = popup;

            Stack.Push("p1");
            Assert.AreSame(Created[0], opened);

            Stack.Pop();
            Assert.AreSame(Created[0], closed);
        }
    }
}
