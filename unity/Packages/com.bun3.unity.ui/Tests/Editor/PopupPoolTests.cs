using System.Threading;
using Bun3.Unity.UI.Popups;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupPoolTests : PopupStackTestFixture
    {
        private int _loads;

        private UniTask<Popup> Loader(PopupKey key, CancellationToken cancellationToken)
        {
            _loads++;
            return CreatePopup(key, cancellationToken);
        }

        [SetUp]
        public void SetUpPool() => _loads = 0;

        [Test]
        public void RentAfterReturn_ReusesInstance_ForPooledKey()
        {
            var pool = new PopupPool(Loader);
            var stack = new PopupStack(pool.RentAsync, pool.Return);
            pool.MarkPooled("p1");

            stack.Push("p1");
            var first = stack.Top;
            Assert.AreEqual(1, _loads);

            stack.Pop();

            Assert.IsTrue(first, "Pooled keys must not be destroyed.");
            Assert.IsFalse(first.gameObject.activeSelf, "Returned instances must be deactivated.");

            stack.Push("p1");

            Assert.AreEqual(1, _loads, "A pool hit must not call the loader again.");
            Assert.AreSame(first, stack.Top);
            Assert.IsTrue(first.gameObject.activeSelf);
            Assert.AreEqual(PopupPhase.Open, first.Phase, "A reuse session must transition normally too.");

            stack.Dispose();
            pool.Dispose();
        }

        [Test]
        public void Return_UnpooledKey_Destroys()
        {
            var pool = new PopupPool(Loader);
            var stack = new PopupStack(pool.RentAsync, pool.Return);

            stack.Push("p1");
            var first = stack.Top;

            stack.Pop();

            Assert.IsFalse((bool)first, "Unpooled keys must be destroyed on return.");

            stack.Dispose();
            pool.Dispose();
        }

        [Test]
        public void Preload_StocksInactiveInstance_RentSkipsLoader()
        {
            var pool = new PopupPool(Loader);
            var stack = new PopupStack(pool.RentAsync, pool.Return);

            pool.PreloadAsync("p1").GetAwaiter().GetResult();

            Assert.AreEqual(1, _loads);
            Assert.IsFalse(Created[0].gameObject.activeSelf, "Preloaded instances must be stored deactivated.");

            stack.Push("p1");

            Assert.AreEqual(1, _loads, "The preloaded instance must be used.");
            Assert.AreSame(Created[0], stack.Top);
            Assert.IsTrue(Created[0].gameObject.activeSelf);

            stack.Dispose();
            pool.Dispose();
        }

        [Test]
        public void PoolWithStack_ArgDeliveredOnEveryRent()
        {
            var pool = new PopupPool(Loader);
            var stack = new PopupStack(pool.RentAsync, pool.Return);
            pool.MarkPooled("p1");

            stack.PushWithArg("p1", arg: 10);
            var popup = (TestPopup)stack.Top;
            stack.Pop();

            stack.PushWithArg("p1", arg: 20);

            Assert.AreSame(popup, stack.Top);
            Assert.AreEqual(20, popup.ReceivedArg, "The arg must be delivered again in a reuse session.");

            stack.Dispose();
            pool.Dispose();
        }

        [Test]
        public void Dispose_DestroysStoredInstances()
        {
            var pool = new PopupPool(Loader);
            pool.PreloadAsync("p1", 2).GetAwaiter().GetResult();

            pool.Dispose();

            Assert.IsFalse((bool)Created[0]);
            Assert.IsFalse((bool)Created[1]);
        }
    }

    public class PopupSiblingArrangerTests : PopupStackTestFixture
    {
        [Test]
        public void Arrange_SortsSiblings_AndNotifiesTopmost()
        {
            var parent = new GameObject("popup-parent").transform;
            try
            {
                Stack.Push("p1", layer: 0);
                Stack.Push("p2", layer: 10);
                Stack.Push("p3", layer: 0);

                foreach (var popup in Created)
                    popup.transform.SetParent(parent, false);

                using var arranger = new PopupSiblingArranger(Stack);
                arranger.Arrange();

                // Stack order: [1, 3, 2(layer10)] → sibling indices in the same order.
                Assert.AreEqual(0, Created[0].transform.GetSiblingIndex());
                Assert.AreEqual(1, Created[2].transform.GetSiblingIndex());
                Assert.AreEqual(2, Created[1].transform.GetSiblingIndex());

                Assert.IsTrue(Created[1].LastIsTopmost);
                Assert.IsFalse(Created[0].LastIsTopmost);
                Assert.IsFalse(Created[2].LastIsTopmost);

                Stack.Pop(); // Topmost (2) closes → auto rearrange.

                Assert.AreEqual(0, Created[0].transform.GetSiblingIndex());
                Assert.AreEqual(1, Created[2].transform.GetSiblingIndex());
                Assert.IsTrue(Created[2].LastIsTopmost, "The new topmost must be notified after a close.");
                Assert.AreEqual(1, Created[2].LastOrderIndex);
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
            }
        }
    }
}
