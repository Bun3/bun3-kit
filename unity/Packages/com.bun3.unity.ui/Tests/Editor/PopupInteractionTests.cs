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

            Assert.IsNotNull(group, "전환 차단용 CanvasGroup이 붙어야 한다.");
            Assert.IsFalse(group.blocksRaycasts, "열림 연출 중에는 차단돼야 한다.");

            popup.OpenSource.TrySetResult();

            Assert.IsTrue(group.blocksRaycasts, "열림 완료 후 복원돼야 한다.");
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

            Assert.IsFalse(group.blocksRaycasts, "닫힘 연출 중에는 차단돼야 한다.");

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
                Assert.AreEqual(PopupPhase.Open, popup.Phase, "잠금 중 딤 클릭은 예약만 돼야 한다.");
            }

            Assert.AreEqual(PopupPhase.None, popup.Phase, "잠금 해제 시 예약된 닫기가 실행돼야 한다.");

            Stack.Push("p2");
            var second = Created[1];
            ExecuteEvents.Execute<IPointerClickHandler>(second.BackgroundDim, eventData,
                ExecuteEvents.pointerClickHandler);

            Assert.AreEqual(0, Stack.Count, "딤 클릭으로 닫혀야 한다.");
        }

        [Test]
        public void TopmostHooks_FireOnCoverAndReveal()
        {
            Stack.Push("p1");
            var first = Created[0];
            Assert.AreEqual(1, first.TopmostGained, "처음 열릴 때도 최상단 진입으로 친다.");

            Stack.Push("p2");
            Assert.AreEqual(1, first.TopmostLost, "위가 열리면 가려짐 통지가 와야 한다.");
            Assert.AreEqual(1, Created[1].TopmostGained);

            Stack.Pop();
            Assert.AreEqual(2, first.TopmostGained, "위가 닫히면 드러남 통지가 와야 한다.");
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

            Stack.Pop(); // p1 닫힘 → 대기열의 p2가 표시되므로 아직 비지 않음

            Assert.AreEqual(0, emptied, "대기열이 이어지는 동안은 빈 것이 아니다.");
            Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending, waiting.Status);

            Stack.Pop(); // p2 닫힘 → 진짜 빈 상태

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
