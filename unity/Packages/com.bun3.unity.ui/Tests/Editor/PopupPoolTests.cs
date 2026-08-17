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

        private UniTask<PopupBehaviour> Loader(PopupKey key, CancellationToken cancellationToken)
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
            pool.MarkPooled(1);

            stack.Push(1);
            var first = stack.Top;
            Assert.AreEqual(1, _loads);

            stack.Pop();

            Assert.IsTrue(first, "풀 대상 키는 파괴되지 않아야 한다.");
            Assert.IsFalse(first.gameObject.activeSelf, "반납된 인스턴스는 비활성화돼야 한다.");

            stack.Push(1);

            Assert.AreEqual(1, _loads, "풀 히트면 로더가 다시 불리면 안 된다.");
            Assert.AreSame(first, stack.Top);
            Assert.IsTrue(first.gameObject.activeSelf);
            Assert.AreEqual(PopupPhase.Open, first.Phase, "재사용 세션도 정상 전이해야 한다.");

            stack.Dispose();
            pool.Dispose();
        }

        [Test]
        public void Return_UnpooledKey_Destroys()
        {
            var pool = new PopupPool(Loader);
            var stack = new PopupStack(pool.RentAsync, pool.Return);

            stack.Push(1);
            var first = stack.Top;

            stack.Pop();

            Assert.IsFalse((bool)first, "풀 대상이 아닌 키는 반납 시 파괴돼야 한다.");

            stack.Dispose();
            pool.Dispose();
        }

        [Test]
        public void Preload_StocksInactiveInstance_RentSkipsLoader()
        {
            var pool = new PopupPool(Loader);
            var stack = new PopupStack(pool.RentAsync, pool.Return);

            pool.PreloadAsync(1).GetAwaiter().GetResult();

            Assert.AreEqual(1, _loads);
            Assert.IsFalse(Created[0].gameObject.activeSelf, "프리로드 인스턴스는 비활성 보관돼야 한다.");

            stack.Push(1);

            Assert.AreEqual(1, _loads, "프리로드된 인스턴스를 써야 한다.");
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
            pool.MarkPooled(1);

            stack.PushWithArg(1, arg: 10);
            var popup = (TestPopup)stack.Top;
            stack.Pop();

            stack.PushWithArg(1, arg: 20);

            Assert.AreSame(popup, stack.Top);
            Assert.AreEqual(20, popup.ReceivedArg, "재사용 세션에도 인자가 다시 전달돼야 한다.");

            stack.Dispose();
            pool.Dispose();
        }

        [Test]
        public void Dispose_DestroysStoredInstances()
        {
            var pool = new PopupPool(Loader);
            pool.PreloadAsync(1, 2).GetAwaiter().GetResult();

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
                Stack.Push(1, layer: 0);
                Stack.Push(2, layer: 10);
                Stack.Push(3, layer: 0);

                foreach (var popup in Created)
                    popup.transform.SetParent(parent, false);

                using var arranger = new PopupSiblingArranger(Stack);
                arranger.Arrange();

                // 스택 순서: [1, 3, 2(layer10)] → sibling index도 같은 순서.
                Assert.AreEqual(0, Created[0].transform.GetSiblingIndex());
                Assert.AreEqual(1, Created[2].transform.GetSiblingIndex());
                Assert.AreEqual(2, Created[1].transform.GetSiblingIndex());

                Assert.IsTrue(Created[1].LastIsTopmost);
                Assert.IsFalse(Created[0].LastIsTopmost);
                Assert.IsFalse(Created[2].LastIsTopmost);

                Stack.Pop(); // 최상단(2) 닫힘 → 자동 재정렬

                Assert.AreEqual(0, Created[0].transform.GetSiblingIndex());
                Assert.AreEqual(1, Created[2].transform.GetSiblingIndex());
                Assert.IsTrue(Created[2].LastIsTopmost, "닫힌 뒤 새 최상단이 통지받아야 한다.");
                Assert.AreEqual(1, Created[2].LastOrderIndex);
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
            }
        }
    }
}
