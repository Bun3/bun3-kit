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
            _view = null; // NUnit 픽스처 재사용 — 이전 테스트의 파괴된 참조 제거
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

            _view.WaitSource.TrySetResult(); // a의 유지 시간 종료

            Assert.AreEqual("b", _view.Last, "앞이 끝나면 다음이 표시돼야 한다.");
            Assert.AreEqual(0, _queue.PendingCount);

            _view.WaitSource.TrySetResult();

            Assert.IsFalse(_queue.IsShowing);
            Assert.IsFalse(_view.gameObject.activeSelf, "다 끝나면 뷰는 비활성 보관된다.");
        }

        [Test]
        public void Show_OverCapacity_Dropped()
        {
            _queue.Show("a");           // 표시 중
            _queue.Show("b");
            _queue.Show("c");           // pending 2 = capacity

            Assert.IsFalse(_queue.Show("d"), "대기 상한 초과분은 버려져야 한다.");
            Assert.AreEqual(2, _queue.PendingCount);
        }

        [Test]
        public void Show_Duplicate_Suppressed()
        {
            _queue.Show("a");

            Assert.IsFalse(_queue.Show("a"), "표시 중인 것과 같은 데이터는 억제돼야 한다.");

            _queue.Show("b");
            Assert.IsFalse(_queue.Show("b"), "대기 중인 것과 같은 데이터도 억제돼야 한다.");
        }

        [Test]
        public void Show_Force_JumpsQueue_AndSkipsCurrent()
        {
            _queue.Show("a");
            _queue.Show("b");

            Assert.IsTrue(_queue.Show("c", force: true));

            Assert.AreEqual("c", _view.Last, "force는 표시 중을 건너뛰고 즉시 표시돼야 한다.");
            Assert.AreEqual(1, _queue.PendingCount, "b는 뒤에 남는다.");
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
