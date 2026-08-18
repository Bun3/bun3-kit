using System.Threading;
using Bun3.Unity.UI.Loading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.UI.Editor.Tests
{
    public class LoadingOverlayTests
    {
        private sealed class TestLoadingView : LoadingView
        {
            public float LastProgress = -1f;

            protected override void OnProgress(float progress01) => LastProgress = progress01;
        }

        private sealed class TestLoadingOverlay : LoadingOverlay
        {
            public UniTaskCompletionSource DelaySource;

            public TestLoadingOverlay(ViewFactory factory) : base(factory, showDelay: 0.2f) { }

            protected override UniTask DelayAsync(float seconds, CancellationToken cancellationToken)
                => (DelaySource = new UniTaskCompletionSource()).Task;
        }

        private TestLoadingView _view;
        private TestLoadingOverlay _overlay;

        private UniTask<LoadingView> CreateView(CancellationToken cancellationToken)
        {
            _view = new GameObject("loading").AddComponent<TestLoadingView>();
            return UniTask.FromResult<LoadingView>(_view);
        }

        [SetUp]
        public void SetUp()
        {
            _view = null; // NUnit 픽스처 재사용 — 이전 테스트의 파괴된 참조 제거
            _overlay = new TestLoadingOverlay(CreateView);
        }

        [TearDown]
        public void TearDown()
        {
            _overlay.Dispose();
            if (_view)
                Object.DestroyImmediate(_view.gameObject);
        }

        [Test]
        public void ShortWork_NeverShows()
        {
            var scope = _overlay.Begin();
            scope.Dispose(); // 지연 시간 안에 끝난 작업

            _overlay.DelaySource.TrySetResult();

            Assert.IsFalse(_overlay.IsVisible, "지연 안에 끝나면 오버레이가 뜨지 않아야 한다(플래시 방지).");
            Assert.IsNull(_view, "뷰 생성조차 하지 않아야 한다.");
        }

        [Test]
        public void LongWork_ShowsAfterDelay_HidesOnRelease()
        {
            var scope = _overlay.Begin();
            _overlay.DelaySource.TrySetResult(); // 지연 경과

            Assert.IsTrue(_overlay.IsVisible);
            Assert.IsTrue(_view.gameObject.activeSelf);

            scope.Dispose();

            Assert.IsFalse(_overlay.IsVisible);
            Assert.IsFalse(_view.gameObject.activeSelf);
        }

        [Test]
        public void NestedScopes_HideOnlyAtZero()
        {
            var outer = _overlay.Begin();
            _overlay.DelaySource.TrySetResult();
            var inner = _overlay.Begin();

            inner.Dispose();
            Assert.IsTrue(_overlay.IsVisible, "구간이 남아 있으면 유지돼야 한다.");

            outer.Dispose();
            Assert.IsFalse(_overlay.IsVisible);
        }

        [Test]
        public void During_WrapsTask()
        {
            var work = new UniTaskCompletionSource<int>();
            var task = _overlay.During(work.Task);
            _overlay.DelaySource.TrySetResult();

            Assert.IsTrue(_overlay.IsVisible);
            Assert.AreEqual(1, _overlay.ActiveCount);

            work.TrySetResult(42);

            Assert.AreEqual(42, task.GetAwaiter().GetResult());
            Assert.IsFalse(_overlay.IsVisible);
        }

        [Test]
        public void SetProgress_ForwardsToView()
        {
            var scope = _overlay.Begin();
            _overlay.DelaySource.TrySetResult();

            _overlay.SetProgress(0.5f);

            Assert.AreEqual(0.5f, _view.LastProgress);
            scope.Dispose();
        }
    }
}
