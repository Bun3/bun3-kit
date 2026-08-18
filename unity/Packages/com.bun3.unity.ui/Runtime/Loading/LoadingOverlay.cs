using System;
using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Loading
{
    /// <summary>
    /// 전역 로딩 오버레이(레거시 Popup_Loading 대응). <b>ref-count</b>라 중첩된
    /// Show/Hide(동시 패킷 2건 등)에도 안전하고, <b>지연 표시</b>로 짧은 작업의
    /// 오버레이 깜빡임(플래시)을 막는다.
    /// </summary>
    /// <example><code>
    /// using (loading.Begin())                     // ~Scope 관례 — 예외에도 해제
    ///     await SendPacketAsync(req, ct);
    /// var data = await loading.During(FetchAsync(ct));   // 한 줄 래핑
    /// loading.SetProgress(0.7f);                  // 진행률 UI가 있으면
    /// </code></example>
    public class LoadingOverlay : IDisposable
    {
        /// <summary>오버레이 뷰를 만든다(1회 지연 생성 후 재사용). 부모 배치는 게임 몫.</summary>
        public delegate UniTask<LoadingView> ViewFactory(CancellationToken cancellationToken);

        private readonly ViewFactory _factory;
        private readonly float _showDelay;

        private LoadingView _view;
        private CancellationTokenSource _lifetime = new();
        private int _activeCount;
        private int _sessionVersion; // 0으로 떨어질 때마다 증가 — 지연 표시 태스크 무효화
        private bool _visible;
        private bool _disposed;

        /// <param name="factory">뷰 생성(1회).</param>
        /// <param name="showDelay">
        /// 표시까지의 지연(초). 이 시간 안에 작업이 끝나면 오버레이를 아예 띄우지 않는다
        /// (레거시 0.2s 플래시 방지). 0이면 즉시 표시.
        /// </param>
        public LoadingOverlay(ViewFactory factory, float showDelay = 0.2f)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _showDelay = showDelay;
        }

        /// <summary>진행 중인 로딩 구간 수.</summary>
        public int ActiveCount => _activeCount;

        /// <summary>오버레이가 실제로 화면에 떠 있는지(지연 표시 대기 중이면 false).</summary>
        public bool IsVisible => _visible;

        /// <summary>
        /// 로딩 구간을 연다(중첩 가능). 반환된 스코프를 <c>using</c>으로 감싸면
        /// 예외가 나도 구간이 닫힌다. 첫 구간이 열리면 지연 표시가 시작된다.
        /// </summary>
        public LoadingScope Begin()
        {
            ThrowIfDisposed();

            _activeCount++;
            if (_activeCount == 1)
                ShowAfterDelayAsync(_sessionVersion, _lifetime.Token).Forget();

            return new LoadingScope(this);
        }

        /// <summary>태스크가 끝날 때까지 로딩 구간을 유지한다.</summary>
        public async UniTask During(UniTask task)
        {
            using (Begin())
                await task;
        }

        /// <summary>태스크가 끝날 때까지 로딩 구간을 유지하고 결과를 돌려준다.</summary>
        public async UniTask<T> During<T>(UniTask<T> task)
        {
            using (Begin())
                return await task;
        }

        /// <summary>진행률(0~1)을 뷰에 전달한다. 뷰가 아직 없으면 무시.</summary>
        public void SetProgress(float progress01)
        {
            if (_view)
                _view.OnProgress(progress01);
        }

        /// <summary>진행 취소 + 뷰 파괴. 이후 <see cref="Begin"/>은 예외.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();

            if (_view)
                _view.gameObject.SafeDestroy();
        }

        /// <summary>지연 대기 지점 — 테스트에서 수동 완료 소스로 오버라이드한다.</summary>
        protected virtual UniTask DelayAsync(float seconds, CancellationToken cancellationToken)
            => UniTask.Delay(TimeSpan.FromSeconds(seconds), ignoreTimeScale: true,
                cancellationToken: cancellationToken);

        internal void Release()
        {
            if (_disposed || _activeCount == 0)
                return;

            _activeCount--;
            if (_activeCount > 0)
                return;

            _sessionVersion++; // 아직 표시 전이면 지연 태스크를 무효화 — 플래시 방지의 핵심.

            if (_visible)
                HideAsync(_lifetime.Token).Forget();
        }

        private async UniTaskVoid ShowAfterDelayAsync(int version, CancellationToken cancellationToken)
        {
            if (_showDelay > 0f)
            {
                bool canceled = await DelayAsync(_showDelay, cancellationToken)
                    .SuppressCancellationThrow();
                if (canceled)
                    return;
            }

            // 지연 사이에 구간이 전부 닫혔으면(버전 증가) 띄우지 않는다.
            if (version != _sessionVersion || _activeCount == 0)
                return;

            var view = await GetViewAsync(cancellationToken);
            if (view == null || version != _sessionVersion || _activeCount == 0)
                return;

            _visible = true;
            view.gameObject.SetActive(true);
            await view.PlayShowAsync(cancellationToken).SuppressCancellationThrow();
        }

        private async UniTaskVoid HideAsync(CancellationToken cancellationToken)
        {
            _visible = false;

            if (_view)
            {
                await _view.PlayHideAsync(cancellationToken).SuppressCancellationThrow();
                if (_view && !_visible) // 숨김 연출 중 새 구간이 다시 띄웠으면 끄지 않는다.
                    _view.gameObject.SetActive(false);
            }
        }

        private async UniTask<LoadingView> GetViewAsync(CancellationToken cancellationToken)
        {
            if (_view)
                return _view;

            try
            {
                _view = await _factory(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (_view)
                _view.gameObject.SetActive(false);

            return _view;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LoadingOverlay));
        }
    }

    /// <summary>
    /// <see cref="LoadingOverlay.Begin"/>이 돌려주는 로딩 구간 스코프.
    /// Dispose가 구간 하나를 닫는다. 복사하지 말 것.
    /// </summary>
    public struct LoadingScope : IDisposable
    {
        private LoadingOverlay _overlay;

        internal LoadingScope(LoadingOverlay overlay) => _overlay = overlay;

        /// <summary>구간 하나를 닫는다. 두 번 Dispose해도 한 번만 반영된다.</summary>
        public void Dispose()
        {
            var overlay = _overlay;
            _overlay = null;
            overlay?.Release();
        }
    }
}
