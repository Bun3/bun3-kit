using System;
using Bun3.Unity.UI.Popups;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class PopupManagerTests : PopupStackTestFixture
    {
        [TearDown]
        public void ClearInstance() => PopupManager.Instance = null;

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
                Assert.AreSame(manager.Stack, manager.BackKeyRouter.Stack, "The stack must be injected into the router.");

                manager.Pool.MarkPooled("p1");
                manager.Stack.Push("p1");
                var first = manager.Stack.Top;
                manager.Stack.Pop();
                manager.Stack.Push("p1");

                Assert.AreSame(first, manager.Stack.Top, "The pool must be wired to the stack so the instance is reused.");

                manager.Dispose();

                Assert.IsFalse((bool)manager.BackKeyRouter, "Dispose must remove the router component.");
                Assert.Throws<ObjectDisposedException>(() => manager.Stack.Push("p1"));
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

            manager.Stack.Push("p1");
            Assert.AreEqual(1, manager.Stack.Count);

            manager.Dispose();
        }

        [Test]
        public void Facade_DelegatesToStack()
        {
            var manager = new PopupManagerBuilder(CreatePopup).Build();

            manager.Push("p1");

            Assert.AreEqual(1, manager.Count);
            Assert.AreSame(manager.Stack.Top, manager.Top);
            Assert.IsTrue(manager.IsOpen("p1"));
            Assert.IsTrue(manager.HandleBack());
            Assert.AreEqual(0, manager.Count);

            manager.Dispose();
        }

        [Test]
        public void Instance_ClearedOnDispose_OnlyIfSelf()
        {
            var first = new PopupManagerBuilder(CreatePopup).Build();
            var second = new PopupManagerBuilder(CreatePopup).Build();
            PopupManager.Instance = first;

            second.Dispose();
            Assert.AreSame(first, PopupManager.Instance, "Dispose of another instance must not touch the slot.");

            first.Dispose();
            Assert.IsNull(PopupManager.Instance, "Dispose must clear the slot when it holds itself.");
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
            Stack.Push("p1");

            Assert.IsTrue(Created[0].BackgroundDim.activeSelf, "Alone, its own dim turns on.");

            Stack.Push("p2");

            Assert.IsFalse(Created[0].BackgroundDim.activeSelf);
            Assert.IsTrue(Created[1].BackgroundDim.activeSelf, "Always exactly one dim — the topmost owner's.");

            Stack.Pop();

            Assert.IsTrue(Created[0].BackgroundDim.activeSelf, "The dim returns to the owner below when the top closes.");
        }

        [Test]
        public void Dim_DimlessTopmost_KeepsLowerOwnersDim()
        {
            WithDim = true;
            Stack.Push("p1");

            WithDim = false;
            Stack.Push("p2"); // A dimless popup is topmost.

            Assert.IsNull(Created[1].BackgroundDim);
            Assert.IsTrue(Created[0].BackgroundDim.activeSelf,
                "When the topmost has no dim, the dim of the dim-owning popup below must stay on.");

            Stack.Pop();

            Assert.IsTrue(Created[0].BackgroundDim.activeSelf);
        }
    }
}
