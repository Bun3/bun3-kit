using System;
using System.Threading;
using Bun3.Unity.UI.Toasts;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class ToastQueueTests
    {
        private sealed class TestToast : ToastView<string>
        {
            public string Last;
            public int BindCount;
            public UniTaskCompletionSource WaitSource;

            protected override void OnData(string data)
            {
                Last = data;
                BindCount++;
            }

            protected override UniTask WaitAsync(float duration, CancellationToken cancellationToken)
                => (WaitSource = new UniTaskCompletionSource()).Task;
        }

        private TestToast _view;
        private ToastQueue<string> _queue;

        private UniTask<ToastView<string>> CreateView(CancellationToken cancellationToken)
        {
            _view = new GameObject("toast").AddComponent<TestToast>();
            return UniTask.FromResult<ToastView<string>>(_view);
        }

        [SetUp]
        public void SetUp()
        {
            _view = null; // NUnit reuses the fixture — drop the previous test's destroyed reference.
            _queue = new ToastQueue<string>(CreateView, defaultDuration: 2f, capacity: 2,
                duplicateComparer: StringComparer.Ordinal);
        }

        [TearDown]
        public void TearDown()
        {
            _queue.Dispose();
            if (_view)
                UnityEngine.Object.DestroyImmediate(_view.gameObject);
        }

        [Test]
        public void Show_DisplaysSequentially_OneAtATime()
        {
            Assert.IsTrue(_queue.Show("a"));
            Assert.IsTrue(_queue.Show("b"));

            Assert.IsTrue(_queue.IsShowing);
            Assert.AreEqual("a", _view.Last);
            Assert.IsTrue(_view.gameObject.activeSelf);
            Assert.AreEqual(1, _queue.PendingCount);

            _view.WaitSource.TrySetResult(); // End a's hold time.

            Assert.AreEqual("b", _view.Last, "The next toast must show when the previous finishes.");
            Assert.AreEqual(0, _queue.PendingCount);

            _view.WaitSource.TrySetResult();

            Assert.IsFalse(_queue.IsShowing);
            Assert.IsFalse(_view.gameObject.activeSelf, "The view is kept deactivated when everything is done.");
        }

        [Test]
        public void Show_OverCapacity_Dropped()
        {
            _queue.Show("a");           // Showing.
            _queue.Show("b");
            _queue.Show("c");           // pending 2 = capacity

            Assert.IsFalse(_queue.Show("d"), "Requests over the pending cap must be dropped.");
            Assert.AreEqual(2, _queue.PendingCount);
        }

        [Test]
        public void Show_Duplicate_Suppressed()
        {
            _queue.Show("a");

            Assert.IsFalse(_queue.Show("a"), "Data equal to the showing toast must be suppressed.");

            _queue.Show("b");
            Assert.IsFalse(_queue.Show("b"), "Data equal to a pending toast must also be suppressed.");
        }

        [Test]
        public void Show_Force_JumpsQueue_AndSkipsCurrent()
        {
            _queue.Show("a");
            _queue.Show("b");

            Assert.IsTrue(_queue.Show("c", force: true));

            Assert.AreEqual("c", _view.Last, "Force must skip the showing toast and display immediately.");
            Assert.AreEqual(1, _queue.PendingCount, "b stays behind.");
        }

        [Test]
        public void Clear_DropsPending_KeepsCurrent()
        {
            _queue.Show("a");
            _queue.Show("b");

            _queue.Clear();

            Assert.IsTrue(_queue.IsShowing);
            Assert.AreEqual(0, _queue.PendingCount);

            _view.WaitSource.TrySetResult();
            Assert.IsFalse(_queue.IsShowing);
        }
    }
}
