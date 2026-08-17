using Bun3.Unity.UI.Popups;
using NUnit.Framework;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupFocusAndQueueTests : PopupStackTestFixture
    {
        [Test]
        public void Focus_ReusesExistingInstance_AndMovesToTop()
        {
            Stack.Push(1);
            Stack.Push(2);
            PopupBehaviour focused = null;
            Stack.Focused += popup => focused = popup;

            var result = Stack.PushAsync(1, duplicate: PopupDuplicatePolicy.Focus)
                .GetAwaiter().GetResult();

            Assert.AreSame(Created[0], result, "새로 만들지 않고 기존 인스턴스를 돌려줘야 한다.");
            Assert.AreEqual(2, Created.Count);
            Assert.AreSame(Created[0], Stack.Top, "기존 인스턴스가 최상단으로 이동해야 한다.");
            Assert.AreSame(Created[0], focused);
        }

        [Test]
        public void Focus_WithArg_RedeliversToExisting()
        {
            Stack.PushWithArg(1, arg: 10);

            Stack.PushWithArgAsync(1, arg: 20, duplicate: PopupDuplicatePolicy.Focus)
                .GetAwaiter().GetResult();

            Assert.AreEqual(1, Created.Count);
            Assert.AreEqual(20, Created[0].ReceivedArg, "인자가 기존 인스턴스에 재주입돼야 한다.");
        }

        [Test]
        public void Focus_RespectsLayer_StaysBelowHigherLayer()
        {
            Stack.Push(1, layer: 0);
            Stack.Push(2, layer: 10);

            Stack.PushAsync(1, duplicate: PopupDuplicatePolicy.Focus).GetAwaiter().GetResult();

            Assert.AreSame(Created[1], Stack.Top, "Focus는 자기 레이어 안에서만 최상단으로 간다.");
        }

        [Test]
        public void PopupsView_MatchesStackOrder()
        {
            Stack.Push(1, layer: 0);
            Stack.Push(2, layer: 10);
            Stack.Push(3, layer: 0);

            var view = Stack.Popups;

            Assert.AreEqual(3, view.Count);
            Assert.AreSame(Created[0], view[0]);
            Assert.AreSame(Created[2], view[1]);
            Assert.AreSame(Created[1], view[2]);
        }

        [Test]
        public void PopupQueue_ShowsOnTopOfOtherPopups_OneAtATime()
        {
            Stack.Push(1); // 우편함 격 — 계속 열려 있음
            var queue = new PopupQueue(Stack);

            queue.Enqueue(2);
            queue.Enqueue(3);

            // 스택 대기열과 달리, 다른 팝업이 열려 있어도 즉시 표시된다.
            Assert.AreEqual(2, Stack.Count);
            Assert.AreSame(Created[1], Stack.Top);
            Assert.AreEqual(1, queue.Count);
            Assert.AreSame(Created[1], queue.Current);

            Stack.Close(Created[1]);

            Assert.AreSame(Created[2], Stack.Top, "이 큐의 팝업이 닫히면 다음이 표시돼야 한다.");
            Assert.AreEqual(0, queue.Count);

            Stack.Close(Created[2]);

            Assert.AreEqual(1, Stack.Count, "밑에 깔려 있던 팝업은 그대로다.");
            Assert.IsNull(queue.Current);
        }

        [Test]
        public void PopupQueue_HigherPriorityShowsFirst_FifoWithinSame()
        {
            Stack.Push(1); // 큐 표시를 막지 않는 배경 팝업
            var queue = new PopupQueue(Stack);
            Stack.Close(Created[0]);

            // 표시 중인 팝업이 없으니 첫 Enqueue가 즉시 열린다. 그 뒤에 쌓이는 항목들로 정렬 검증.
            queue.Enqueue(2, priority: 0);          // 즉시 표시
            queue.Enqueue(3, priority: 0);          // 일반
            queue.Enqueue(4, priority: 2);          // 승급 격
            queue.Enqueue(5, priority: 1);          // 특별 아이템 격

            Stack.Close(Created[1]);
            Assert.AreEqual(4, Stack.Top.Key.Value, "우선순위 높은 항목이 먼저 표시돼야 한다.");

            Stack.Pop();
            Assert.AreEqual(5, Stack.Top.Key.Value);

            Stack.Pop();
            Assert.AreEqual(3, Stack.Top.Key.Value, "같은 우선순위는 삽입 순서를 지켜야 한다.");
        }

        [Test]
        public void PopupQueue_WithArg_Delivers()
        {
            var queue = new PopupQueue(Stack);

            queue.EnqueueWithArg(1, arg: 77);

            Assert.AreEqual(77, Created[0].ReceivedArg);
        }

        [Test]
        public void PopupQueue_Clear_DropsPendingOnly()
        {
            var queue = new PopupQueue(Stack);
            queue.Enqueue(1);
            queue.Enqueue(2);

            queue.Clear();

            Assert.AreEqual(1, Stack.Count, "표시 중인 팝업은 유지돼야 한다.");
            Assert.AreEqual(0, queue.Count);

            Stack.Pop();
            Assert.AreEqual(0, Stack.Count, "버린 항목이 표시되면 안 된다.");
        }
    }
}
