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
                "The data must be delivered before stack insertion (Attach).");
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

            Stack.PushWithArg("p1", arg: 1.5f); // TestPopup does not implement IPopupArg<float>.

            Assert.AreEqual(1, Stack.Count, "The popup itself must still open on a wiring error.");
            Assert.AreEqual(PopupPhase.Open, Created[0].Phase);
        }

        [Test]
        public void MultipleArgInterfaces_DispatchByStaticType()
        {
            // The same popup exposes both a code path (int) and a data path (string token).
            Stack.PushWithArg("p1", arg: 1001);
            Stack.PushWithArg("p2", arg: "1001");

            Assert.AreEqual(1001, Created[0].ReceivedArg);
            Assert.IsNull(Created[0].ReceivedToken);
            Assert.AreEqual("1001", Created[1].ReceivedToken);
            Assert.AreEqual(0, Created[1].ReceivedArg);
        }
    }
}
