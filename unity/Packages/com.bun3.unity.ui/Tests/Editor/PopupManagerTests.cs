using System;
using Bun3.Unity.UI.Popups;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupManagerTests : PopupStackTestFixture
    {
        [Test]
        public void Builder_WiresPoolRouterArranger()
        {
            var host = new GameObject("ui-root");
            try
            {
                var manager = new PopupManagerBuilder(CreatePopup)
                    .UsePool()
                    .UseBackKey(host)
                    .UseSiblingArranger()
                    .Build();

                Assert.IsNotNull(manager.Pool);
                Assert.IsNotNull(manager.Arranger);
                Assert.IsTrue(manager.BackKeyRouter);
                Assert.AreSame(manager.Stack, manager.BackKeyRouter.Stack, "라우터에 스택이 주입돼야 한다.");

                manager.Pool.MarkPooled(1);
                manager.Stack.Push(1);
                var first = manager.Stack.Top;
                manager.Stack.Pop();
                manager.Stack.Push(1);

                Assert.AreSame(first, manager.Stack.Top, "풀이 스택에 배선돼 인스턴스가 재사용돼야 한다.");

                manager.Dispose();

                Assert.IsFalse((bool)manager.BackKeyRouter, "Dispose가 라우터 컴포넌트를 제거해야 한다.");
                Assert.Throws<ObjectDisposedException>(() => manager.Stack.Push(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Builder_MinimalStackOnly()
        {
            var manager = new PopupManagerBuilder(CreatePopup).Build();

            Assert.IsNull(manager.Pool);
            Assert.IsNull(manager.Arranger);
            Assert.IsNull((object)manager.BackKeyRouter);

            manager.Stack.Push(1);
            Assert.AreEqual(1, manager.Stack.Count);

            manager.Dispose();
        }

        [Test]
        public void Builder_PoolWithReleaser_Throws()
        {
            var builder = new PopupManagerBuilder(CreatePopup)
                .UsePool()
                .WithReleaser(ReleasePopup);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }
    }

    public class PopupDimTests : PopupStackTestFixture
    {
        [Test]
        public void Dim_OnlyTopmostDimOwnerShows()
        {
            WithDim = true;
            Stack.Push(1);

            Assert.IsTrue(Created[0].BackgroundDim.activeSelf, "혼자 있으면 자기 딤이 켜진다.");

            Stack.Push(2);

            Assert.IsFalse(Created[0].BackgroundDim.activeSelf);
            Assert.IsTrue(Created[1].BackgroundDim.activeSelf, "딤은 항상 한 장 — 최상단 소유자만.");

            Stack.Pop();

            Assert.IsTrue(Created[0].BackgroundDim.activeSelf, "위가 닫히면 아래 소유자에게 돌아온다.");
        }

        [Test]
        public void Dim_DimlessTopmost_KeepsLowerOwnersDim()
        {
            WithDim = true;
            Stack.Push(1);

            WithDim = false;
            Stack.Push(2); // 딤 없는 팝업이 최상단

            Assert.IsNull(Created[1].BackgroundDim);
            Assert.IsTrue(Created[0].BackgroundDim.activeSelf,
                "최상단이 딤을 안 쓰면 그 아래 딤 보유 팝업의 딤이 유지돼야 한다.");

            Stack.Pop();

            Assert.IsTrue(Created[0].BackgroundDim.activeSelf);
        }
    }
}
