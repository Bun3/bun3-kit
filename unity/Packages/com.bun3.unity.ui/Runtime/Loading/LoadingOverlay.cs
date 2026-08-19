using System;
using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Loading
{
    /// <summary>
    /// Global loading overlay. <b>Ref-counted</b>, so nested Show/Hide (e.g. two concurrent
    /// packets) is safe, and <b>delayed display</b> avoids overlay flashing for short operations.
    /// </summary>
    /// <example><code>
    /// using (loading.Begin())                     // ~Scope convention — releases on exception too
    ///     await SendPacketAsync(req, ct);
    /// var data = await loading.During(FetchAsync(ct));   // one-line wrapping
    /// loading.SetProgress(0.7f);                  // if there is progress UI
    /// </code></example>
    public class LoadingOverlay : IDisposable
    {
        /// <summary>Creates the overlay view (lazy, once, then reused). Parenting is the game's job.</summary>
        public delegate UniTask<LoadingView> ViewFactory(CancellationToken cancellationToken);

        private readonly ViewFactory _factory;
        private readonly float _showDelay;

        private LoadingView _view;
        private CancellationTokenSource _lifetime = new();
        private int _activeCount;
        private int _sessionVersion; // Incremented whenever the count drops to 0 — invalidates delayed-show tasks.
        private bool _visible;
        private bool _disposed;

        /// <param name="factory">View creation (once).</param>
        /// <param name="showDelay">
        /// Delay before showing, in seconds. If the work finishes within it, the overlay never
        /// appears (flash prevention). 0 shows immediately.
        /// </param>
        public LoadingOverlay(ViewFactory factory, float showDelay = 0.2f)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _showDelay = showDelay;
        }

        /// <summary>Number of loading sections in progress.</summary>
        public int ActiveCount => _activeCount;

        /// <summary>Whether the overlay is actually on screen (false while waiting out the show delay).</summary>
        public bool IsVisible => _visible;

        /// <summary>
        /// Opens a loading section (nestable). Wrap the returned scope in <c>using</c> so the
        /// section closes even on exception. The delayed show starts when the first section opens.
        /// </summary>
        public LoadingScope Begin()
        {
            ThrowIfDisposed();

            _activeCount++;
            if (_activeCount == 1)
                ShowAfterDelayAsync(_sessionVersion, _lifetime.Token).Forget();

            return new LoadingScope(this);
        }

        /// <summary>Holds a loading section until the task finishes.</summary>
        public async UniTask During(UniTask task)
        {
            using (Begin())
                await task;
        }

        /// <summary>Holds a loading section until the task finishes and returns its result.</summary>
        public async UniTask<T> During<T>(UniTask<T> task)
        {
            using (Begin())
                return await task;
        }

        /// <summary>Forwards progress (0-1) to the view. Ignored while no view exists.</summary>
        public void SetProgress(float progress01)
        {
            if (_view)
                _view.OnProgress(progress01);
        }

        /// <summary>Cancels in-flight work + destroys the view. <see cref="Begin"/> throws afterward.</summary>
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

        /// <summary>Delay await point — tests override with a manual completion source.</summary>
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

            _sessionVersion++; // Invalidates the delayed task if not yet shown — the core of flash prevention.

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

            // If all sections closed during the delay (version bumped), never show.
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
                if (_view && !_visible) // If a new section re-showed during the hide transition, do not deactivate.
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
    /// Loading-section scope returned by <see cref="LoadingOverlay.Begin"/>.
    /// Dispose closes one section. Do not copy.
    /// </summary>
    public struct LoadingScope : IDisposable
    {
        private LoadingOverlay _overlay;

        internal LoadingScope(LoadingOverlay overlay) => _overlay = overlay;

        /// <summary>Closes one section. A double Dispose counts only once.</summary>
        public void Dispose()
        {
            var overlay = _overlay;
            _overlay = null;
            overlay?.Release();
        }
    }
}
