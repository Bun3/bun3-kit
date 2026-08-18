using System.Text.RegularExpressions;
using Bun3.Unity.UI.Popups;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupStackArgTests : PopupStackTestFixture
    {
        [Test]
        public void PushAsync_ReturnsOpenedInstance()
        {
            var popup = Stack.PushAsync("p1").GetAwaiter().GetResult();

            Assert.AreSame(Created[0], popup);
            Assert.AreEqual(PopupPhase.Open, popup.Phase);
        }

        [Test]
        public void PushAsync_DuplicateIgnore_ReturnsNull()
        {
            Stack.Push("p1");

            var second = Stack.PushAsync("p1").GetAwaiter().GetResult();

            Assert.IsNull(second);
        }

        [Test]
        public void PushArg_DeliveredAfterLoad_BeforeAttach()
        {
            Stack.PushWithArg("p1", arg: 42);

            Assert.AreEqual(42, Created[0].ReceivedArg);
            Assert.AreEqual(PopupPhase.None, Created[0].PhaseAtArg,
                "데이터는 스택 삽입(Attach) 전에 전달돼야 한다.");
            Assert.AreEqual(PopupPhase.Open, Created[0].Phase);
        }

        [Test]
        public void PushAsyncArg_ReturnsInstance()
        {
            var popup = Stack.PushWithArgAsync("p1", arg: 7).GetAwaiter().GetResult();

            Assert.AreSame(Created[0], popup);
            Assert.AreEqual(7, Created[0].ReceivedArg);
        }

        [Test]
        public void EnqueueArg_DeliveredOnDrain()
        {
            Stack.Push("p1");
            Stack.EnqueueWithArg("p2", arg: 7);

            Assert.AreEqual(1, Stack.QueuedCount);

            Stack.Pop();

            Assert.AreEqual(7, Created[1].ReceivedArg);
            Assert.AreSame(Created[1], Stack.Top);
        }

        [Test]
        public void PushArg_QueuePolicy_PreservesArg()
        {
            Stack.Push("p1");
            Stack.PushWithArg("p1", arg: 9, duplicate: PopupDuplicatePolicy.Queue);

            Stack.Pop();

            Assert.AreEqual(9, Created[1].ReceivedArg);
        }

        [Test]
        public void PushArg_WithoutReceiver_LogsErrorAndStillOpens()
        {
            LogAssert.Expect(LogType.Error, new Regex("IPopupArg"));

            Stack.PushWithArg("p1", arg: 1.5f); // TestPopup은 IPopupArg<float> 미구현

            Assert.AreEqual(1, Stack.Count, "결선 오류여도 팝업 자체는 열려야 한다.");
            Assert.AreEqual(PopupPhase.Open, Created[0].Phase);
        }

        [Test]
        public void MultipleArgInterfaces_DispatchByStaticType()
        {
            // 같은 팝업이 코드 경로(int)와 데이터 경로(string 토큰)를 함께 노출한다.
            Stack.PushWithArg("p1", arg: 1001);
            Stack.PushWithArg("p2", arg: "1001");

            Assert.AreEqual(1001, Created[0].ReceivedArg);
            Assert.IsNull(Created[0].ReceivedToken);
            Assert.AreEqual("1001", Created[1].ReceivedToken);
            Assert.AreEqual(0, Created[1].ReceivedArg);
        }
    }
}
