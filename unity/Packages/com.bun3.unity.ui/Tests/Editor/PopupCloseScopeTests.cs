using Bun3.Unity.UI.Popups;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupCloseScopeTests : PopupStackTestFixture
    {
        [Test]
        public void BlockClose_DefersClose_UntilRelease()
        {
            Stack.Push("p1");
            var popup = Created[0];

            var scope = popup.BlockClose();
            Stack.Close(popup);

            Assert.AreEqual(PopupPhase.Open, popup.Phase, "A close while locked must only be deferred.");
            Assert.AreEqual(1, Stack.Count);

            scope.Dispose();

            Assert.AreEqual(PopupPhase.None, popup.Phase, "The deferred close must run when the last lock releases.");
            Assert.AreEqual(0, Stack.Count);
        }

        [Test]
        public void BlockClose_Nested_ClosesOnlyAtZero()
        {
            Stack.Push("p1");
            var popup = Created[0];

            var outer = popup.BlockClose();
            var inner = popup.BlockClose();
            Stack.Close(popup);

            inner.Dispose();

            Assert.AreEqual(PopupPhase.Open, popup.Phase, "Must not close while a lock remains.");

            outer.Dispose();

            Assert.AreEqual(PopupPhase.None, popup.Phase);
        }

        [Test]
        public void BlockClose_WithoutCloseRequest_JustUnlocks()
        {
            Stack.Push("p1");
            var popup = Created[0];

            using (popup.BlockClose())
            {
            }

            Assert.AreEqual(PopupPhase.Open, popup.Phase);
            Assert.AreEqual(2, popup.BlockedChanges, "The lock/unlock hook must fire once each.");
            Assert.IsFalse(popup.LastBlocked);
        }

        [Test]
        public void HandleBack_WhileBlocked_ConsumedWithoutRouting()
        {
            Stack.Push("p1");
            var popup = Created[0];

            using (popup.BlockClose())
            {
                Assert.IsTrue(Stack.HandleBack());
            }

            Assert.AreEqual(0, popup.BackRequests, "OnBackRequested must not be called while locked either.");
            Assert.AreEqual(PopupPhase.Open, popup.Phase);
        }

        [Test]
        public void BlockCloseWhile_ReleasesOnCompletion_ThenRunsRequestedClose()
        {
            Stack.Push("p1");
            var popup = Created[0];
            var work = new UniTaskCompletionSource();

            popup.BlockCloseWhile(work.Task).Forget();
            Stack.Close(popup);

            Assert.AreEqual(PopupPhase.Open, popup.Phase);

            work.TrySetResult();

            Assert.AreEqual(PopupPhase.None, popup.Phase, "The deferred close must run when work completion unlocks.");
        }

        [Test]
        public void BlockCloseWhile_Result_ReleasesOnException()
        {
            Stack.Push("p1");
            var popup = Created[0];
            var work = new UniTaskCompletionSource<int>();

            var wrapped = popup.BlockCloseWhile(work.Task);
            work.TrySetException(new System.InvalidOperationException("boom"));

            Assert.Throws<System.InvalidOperationException>(() => wrapped.GetAwaiter().GetResult());
            Assert.IsFalse(popup.IsCloseBlocked, "The lock must release even on exception.");
        }

        [Test]
        public void BlockClose_DuringOpening_ClosesAfterOpenAndRelease()
        {
            PendingOpen = true;
            Stack.Push("p1");
            var popup = Created[0];

            var scope = popup.BlockClose();
            Stack.Close(popup);
            popup.OpenSource.TrySetResult();

            Assert.AreEqual(PopupPhase.Open, popup.Phase, "Open completes, but locked, so it must not close.");

            scope.Dispose();

            Assert.AreEqual(PopupPhase.None, popup.Phase);
            CollectionAssert.Contains(Released, popup);
            Assert.AreEqual(1, Released.Count);
        }

        [Test]
        public void Clear_WhileBlocked_NotifiesUnblockAndInvalidatesOldScopes()
        {
            var pool = new PopupPool(CreatePopup);
            pool.MarkPooled("p1");
            var stack = new PopupStack(pool.RentAsync, pool.Return);

            stack.Push("p1");
            var popup = (TestPopup)stack.Top;
            var staleScope = popup.BlockClose();

            stack.Clear();

            Assert.IsFalse(popup.IsCloseBlocked);
            Assert.IsFalse(popup.LastBlocked, "The unblock presentation notice must fire on forced release.");

            stack.Push("p1"); // Pool reuse — a new session of the same instance.
            Assert.AreSame(popup, stack.Top);

            using (popup.BlockClose())
            {
                staleScope.Dispose(); // Late release from a previous-session scope.
                Assert.IsTrue(popup.IsCloseBlocked, "A previous-session scope must not unlock the new session.");
            }

            Assert.IsFalse(popup.IsCloseBlocked);

            stack.Dispose();
            pool.Dispose();
        }

        [Test]
        public void Clear_IgnoresCloseScopes()
        {
            Stack.Push("p1");
            var popup = Created[0];
            var scope = popup.BlockClose();

            Stack.Clear();

            Assert.AreEqual(PopupPhase.None, popup.Phase, "Clear ignores locks and force-releases.");
            Assert.AreEqual(0, Stack.Count);

            scope.Dispose(); // A late release must be ignored without throwing.
        }
    }
}
