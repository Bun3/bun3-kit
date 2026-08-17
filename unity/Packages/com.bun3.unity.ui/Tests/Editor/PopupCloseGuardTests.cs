using Bun3.Unity.UI.Popups;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupCloseGuardTests : PopupStackTestFixture
    {
        [Test]
        public void BlockClose_DefersClose_UntilRelease()
        {
            Stack.Push(1);
            var popup = Created[0];

            var guard = popup.BlockClose();
            Stack.Close(popup);

            Assert.AreEqual(PopupPhase.Open, popup.Phase, "잠금 중 닫기는 예약만 돼야 한다.");
            Assert.AreEqual(1, Stack.Count);

            guard.Dispose();

            Assert.AreEqual(PopupPhase.None, popup.Phase, "마지막 잠금 해제 시 예약된 닫기가 실행돼야 한다.");
            Assert.AreEqual(0, Stack.Count);
        }

        [Test]
        public void BlockClose_Nested_ClosesOnlyAtZero()
        {
            Stack.Push(1);
            var popup = Created[0];

            var outer = popup.BlockClose();
            var inner = popup.BlockClose();
            Stack.Close(popup);

            inner.Dispose();

            Assert.AreEqual(PopupPhase.Open, popup.Phase, "잠금이 남아 있으면 아직 닫히면 안 된다.");

            outer.Dispose();

            Assert.AreEqual(PopupPhase.None, popup.Phase);
        }

        [Test]
        public void BlockClose_WithoutCloseRequest_JustUnlocks()
        {
            Stack.Push(1);
            var popup = Created[0];

            using (popup.BlockClose())
            {
            }

            Assert.AreEqual(PopupPhase.Open, popup.Phase);
            Assert.AreEqual(2, popup.BlockedChanges, "잠김/풀림 훅이 한 번씩 호출돼야 한다.");
            Assert.IsFalse(popup.LastBlocked);
        }

        [Test]
        public void HandleBack_WhileBlocked_ConsumedWithoutRouting()
        {
            Stack.Push(1);
            var popup = Created[0];

            using (popup.BlockClose())
            {
                Assert.IsTrue(Stack.HandleBack());
            }

            Assert.AreEqual(0, popup.BackRequests, "잠금 중에는 OnBackRequested도 호출되면 안 된다.");
            Assert.AreEqual(PopupPhase.Open, popup.Phase);
        }

        [Test]
        public void BlockCloseWhile_ReleasesOnCompletion_ThenRunsRequestedClose()
        {
            Stack.Push(1);
            var popup = Created[0];
            var work = new UniTaskCompletionSource();

            popup.BlockCloseWhile(work.Task).Forget();
            Stack.Close(popup);

            Assert.AreEqual(PopupPhase.Open, popup.Phase);

            work.TrySetResult();

            Assert.AreEqual(PopupPhase.None, popup.Phase, "작업 완료로 잠금이 풀리면 예약된 닫기가 실행돼야 한다.");
        }

        [Test]
        public void BlockCloseWhile_Result_ReleasesOnException()
        {
            Stack.Push(1);
            var popup = Created[0];
            var work = new UniTaskCompletionSource<int>();

            var wrapped = popup.BlockCloseWhile(work.Task);
            work.TrySetException(new System.InvalidOperationException("boom"));

            Assert.Throws<System.InvalidOperationException>(() => wrapped.GetAwaiter().GetResult());
            Assert.IsFalse(popup.IsCloseBlocked, "예외가 나도 잠금은 해제돼야 한다.");
        }

        [Test]
        public void BlockClose_DuringOpening_ClosesAfterOpenAndRelease()
        {
            PendingOpen = true;
            Stack.Push(1);
            var popup = Created[0];

            var guard = popup.BlockClose();
            Stack.Close(popup);
            popup.OpenSource.TrySetResult();

            Assert.AreEqual(PopupPhase.Open, popup.Phase, "열림은 완료되지만 잠금 중이라 닫히면 안 된다.");

            guard.Dispose();

            Assert.AreEqual(PopupPhase.None, popup.Phase);
            CollectionAssert.Contains(Released, popup);
            Assert.AreEqual(1, Released.Count);
        }

        [Test]
        public void Clear_WhileBlocked_NotifiesUnblockAndInvalidatesOldGuards()
        {
            var pool = new PopupPool(CreatePopup);
            pool.MarkPooled(1);
            var stack = new PopupStack(pool.RentAsync, pool.Return);

            stack.Push(1);
            var popup = (TestPopup)stack.Top;
            var staleGuard = popup.BlockClose();

            stack.Clear();

            Assert.IsFalse(popup.IsCloseBlocked);
            Assert.IsFalse(popup.LastBlocked, "강제 해제 시 잠금 해제 표현 통지가 와야 한다.");

            stack.Push(1); // 풀 재사용 — 같은 인스턴스의 새 세션
            Assert.AreSame(popup, stack.Top);

            using (popup.BlockClose())
            {
                staleGuard.Dispose(); // 이전 세션 가드의 늦은 해제
                Assert.IsTrue(popup.IsCloseBlocked, "이전 세션 가드가 새 세션 잠금을 풀면 안 된다.");
            }

            Assert.IsFalse(popup.IsCloseBlocked);

            stack.Dispose();
            pool.Dispose();
        }

        [Test]
        public void Clear_IgnoresCloseGuards()
        {
            Stack.Push(1);
            var popup = Created[0];
            var guard = popup.BlockClose();

            Stack.Clear();

            Assert.AreEqual(PopupPhase.None, popup.Phase, "Clear는 잠금을 무시하고 강제 해제한다.");
            Assert.AreEqual(0, Stack.Count);

            guard.Dispose(); // 늦은 해제가 예외 없이 무시되는지
        }
    }
}
