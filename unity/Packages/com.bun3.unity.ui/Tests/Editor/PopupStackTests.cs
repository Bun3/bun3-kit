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
            Stack.Push(1);

            Assert.AreEqual(1, Stack.Count);
            Assert.AreSame(Created[0], Stack.Top);
            Assert.AreEqual(PopupPhase.Open, Created[0].Phase);
            Assert.AreEqual(new PopupKey(1), Created[0].Key);
            Assert.IsTrue(Stack.IsOpen(1));
        }

        [Test]
        public void Push_HigherLayerStaysOnTop_OfLaterLowerLayerPush()
        {
            Stack.Push(1, layer: 0);
            Stack.Push(2, layer: 10);
            Stack.Push(3, layer: 0);

            // 정렬: (layer 오름차순, 삽입 순서) — layer 10이 항상 위.
            Assert.AreSame(Created[1], Stack.Top);
            Assert.AreEqual(3, Stack.Count);
        }

        [Test]
        public void Push_SameLayer_LaterPushIsTop()
        {
            Stack.Push(1);
            Stack.Push(2);

            Assert.AreSame(Created[1], Stack.Top);
        }

        [Test]
        public void Push_DuplicateIgnore_SecondRequestDropped()
        {
            Stack.Push(1);
            Stack.Push(1);

            Assert.AreEqual(1, Stack.Count);
            Assert.AreEqual(1, Created.Count);
        }

        [Test]
        public void Push_DuplicateReplace_ClosesExistingAndOpensNew()
        {
            Stack.Push(1);
            var first = Created[0];

            Stack.Push(1, duplicate: PopupDuplicatePolicy.Replace);

            Assert.AreEqual(1, Stack.Count);
            Assert.AreSame(Created[1], Stack.Top);
            Assert.AreEqual(PopupPhase.None, first.Phase);
            CollectionAssert.Contains(Released, first);
        }

        [Test]
        public void Push_DuplicateQueue_WaitsUntilExistingCloses()
        {
            Stack.Push(1);
            Stack.Push(1, duplicate: PopupDuplicatePolicy.Queue);

            Assert.AreEqual(1, Stack.Count);
            Assert.AreEqual(1, Stack.QueuedCount);

            Stack.Pop();

            Assert.AreEqual(1, Stack.Count, "기존 팝업이 닫히면 대기열의 같은 키가 열려야 한다.");
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
            Stack.Push(1);
            Stack.Push(2);

            Assert.IsTrue(Stack.HandleBack());

            Assert.AreEqual(1, Stack.Count);
            Assert.AreSame(Created[0], Stack.Top);
            Assert.AreEqual(1, Created[1].BackRequests);
            Assert.AreEqual(0, Created[0].BackRequests, "back은 최상단에만 라우팅돼야 한다.");
        }

        [Test]
        public void HandleBack_Rejected_ConsumedButNotClosed()
        {
            Stack.Push(1);
            Created[0].RejectBack = true;

            Assert.IsTrue(Stack.HandleBack());

            Assert.AreEqual(1, Stack.Count);
            Assert.AreEqual(PopupPhase.Open, Created[0].Phase);
        }

        [Test]
        public void HandleBack_DuringOpening_ConsumedWithoutRouting()
        {
            PendingOpen = true;
            Stack.Push(1);

            Assert.AreEqual(PopupPhase.Opening, Created[0].Phase);
            Assert.IsTrue(Stack.HandleBack(), "전이 중에도 키는 소비돼야 한다.");
            Assert.AreEqual(0, Created[0].BackRequests);
            Assert.AreEqual(1, Stack.Count);
        }

        [Test]
        public void Enqueue_DrainsSequentially()
        {
            Stack.Enqueue(1);
            Stack.Enqueue(2);

            Assert.AreEqual(1, Stack.Count, "머리는 즉시 표시돼야 한다.");
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
            Stack.Push(1);
            Stack.Enqueue(2);

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
            Stack.Push(1);
            var popup = Created[0];

            Stack.Close(popup);

            Assert.AreEqual(PopupPhase.Opening, popup.Phase, "열림 연출 중에는 닫기가 예약만 돼야 한다.");
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
            Stack.Push(1);
            var popup = Created[0];

            Stack.Close(popup);
            Stack.Close(popup);

            Assert.AreEqual(PopupPhase.Closing, popup.Phase);

            popup.CloseSource.TrySetResult();

            Assert.AreEqual(0, Stack.Count);
            Assert.AreEqual(1, Released.Count, "릴리저는 한 번만 호출돼야 한다.");
        }

        [Test]
        public void WaitUntilClosedAsync_CompletesOnClose()
        {
            Stack.Push(1);
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
            Stack.Push(1);
            Stack.Push(2);
            Stack.Enqueue(3);
            Stack.Close(Created[1]); // 닫힘 연출 중 상태로 만든다.

            Stack.Clear();

            Assert.AreEqual(0, Stack.Count);
            Assert.AreEqual(0, Stack.QueuedCount);
            Assert.AreEqual(2, Released.Count, "연출 완료를 기다리지 않고 전부 해제돼야 한다.");
            Assert.AreEqual(PopupPhase.None, Created[0].Phase);
            Assert.AreEqual(PopupPhase.None, Created[1].Phase);
        }

        [Test]
        public void Clear_DuringOpening_PushAsyncReturnsNull()
        {
            PendingOpen = true;
            var pushTask = Stack.PushAsync(1);

            Stack.Clear();
            Created[0].OpenSource.TrySetResult();

            Assert.IsNull(pushTask.GetAwaiter().GetResult(),
                "열림 연출 중 Clear되면 이미 해제된 인스턴스 대신 null을 돌려줘야 한다.");
        }

        [Test]
        public void Enqueue_FactoryThrow_ContinuesDraining()
        {
            // 구독자가 있으면 UniTask 기본 예외 로깅이 대체된다 — 테스트 로그 오염 방지.
            static void Swallow(System.Exception _) { }
            UniTaskScheduler.UnobservedTaskException += Swallow;
            try
            {
                var stack = new PopupStack(
                    (key, ct) => key.Value == 1
                        ? throw new System.InvalidOperationException("load fail")
                        : CreatePopup(key, ct),
                    ReleasePopup);

                stack.Push(9);
                stack.Enqueue(1); // 로드가 던지는 키
                stack.Enqueue(2);

                stack.Pop(); // 스택이 비면서 드레인 시작 → 1은 실패, 2로 이어져야 한다

                Assert.AreEqual(1, stack.Count, "실패한 항목 다음이 표시돼야 한다.");
                Assert.AreEqual(2, stack.Top.Key.Value);

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

            stack.Push(1);
            stack.Clear();

            var late = new UnityEngine.GameObject("late").AddComponent<TestPopup>();
            source.TrySetResult(late);

            Assert.AreEqual(0, stack.Count, "취소 후 도착한 인스턴스는 스택에 들어오면 안 된다.");
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

            stack.Push(1);
            stack.Push(1); // 로딩 중 중복 — Ignore
            stack.Enqueue(2);

            Assert.AreEqual(0, stack.Count);
            Assert.AreEqual(1, stack.QueuedCount, "로딩 중에는 대기열이 드레인되면 안 된다.");

            created = new UnityEngine.GameObject("loaded").AddComponent<TestPopup>();
            Created.Add((TestPopup)created);
            source.TrySetResult(created);

            Assert.AreEqual(1, stack.Count, "로딩 중 중복 Push는 무시돼야 한다.");
            Assert.AreSame(created, stack.Top);

            stack.Dispose();
        }

        [Test]
        public void FactoryReturningNull_LeavesStackUsable()
        {
            var stack = new PopupStack((key, ct) => UniTask.FromResult<Popup>(null), ReleasePopup);

            stack.Push(1);

            Assert.AreEqual(0, stack.Count);
            Assert.IsFalse(stack.HandleBack());

            stack.Dispose();
        }

        [Test]
        public void Dispose_ThenPush_Throws()
        {
            Stack.Dispose();

            Assert.Throws<System.ObjectDisposedException>(() => Stack.Push(1));
        }

        [Test]
        public void Events_RaisedOnOpenAndClose()
        {
            Popup opened = null;
            Popup closed = null;
            Stack.Opened += popup => opened = popup;
            Stack.Closed += popup => closed = popup;

            Stack.Push(1);
            Assert.AreSame(Created[0], opened);

            Stack.Pop();
            Assert.AreSame(Created[0], closed);
        }
    }
}
