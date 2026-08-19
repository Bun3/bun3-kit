using System;
using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Toasts
{
    /// <summary>
    /// Sequential toast display queue. Shows one at a time, drops requests over the pending cap,
    /// and supports optional duplicate suppression and forced display (<c>force</c>).
    /// Flows independently of the popup stack — no back-key/dim/stack-ordering participation.
    /// </summary>
    /// <remarks>
    /// The view instance is created once via the factory and reused (SetActive toggling).
    /// Display flow: <c>OnData → PlayShowAsync → WaitAsync(duration) → PlayHideAsync → next</c>.
    /// Low-frequency UI path — small allocations (pending entries, etc.) are OK.
    /// </remarks>
    public sealed class ToastQueue<TData> : IDisposable
    {
        /// <summary>Creates the toast view (once). Loading strategy and parenting are the game's job.</summary>
        public delegate UniTask<ToastView<TData>> ViewFactory(CancellationToken cancellationToken);

        private readonly struct Entry
        {
            public readonly TData Data;
            public readonly float Duration;

            public Entry(TData data, float duration)
            {
                Data = data;
                Duration = duration;
            }
        }

        private readonly ViewFactory _factory;
        private readonly float _defaultDuration;
        private readonly int _capacity;
        private readonly IEqualityComparer<TData> _duplicateComparer;

        private readonly List<Entry> _pending = new();
        private ToastView<TData> _view;
        private CancellationTokenSource _lifetime = new();
        private UniTaskCompletionSource _skip;
        private TData _currentData;
        private bool _hasCurrent;
        private bool _draining;
        private bool _disposed;

        /// <param name="factory">View creation (called once, then reused).</param>
        /// <param name="defaultDuration">Default display time in seconds.</param>
        /// <param name="capacity">Pending cap — requests over it are dropped.</param>
        /// <param name="duplicateComparer">If given, suppresses data equal to the showing/pending ones. Null disables suppression.</param>
        public ToastQueue(ViewFactory factory, float defaultDuration = 2f, int capacity = 10,
            IEqualityComparer<TData> duplicateComparer = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _defaultDuration = defaultDuration;
            _capacity = capacity;
            _duplicateComparer = duplicateComparer;
        }

        /// <summary>Number of entries waiting to be shown.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>Whether a toast is currently showing.</summary>
        public bool IsShowing => _hasCurrent;

        /// <summary>
        /// Requests a toast. Shows immediately when possible, otherwise waits its turn.
        /// </summary>
        /// <param name="data">Display data.</param>
        /// <param name="duration">Display time in seconds. Negative uses the default.</param>
        /// <param name="force">True cuts to the front of the queue; a showing toast skips straight to hide.</param>
        /// <returns>Whether accepted — false when dropped by duplicate suppression or the pending cap.</returns>
        public bool Show(TData data, float duration = -1f, bool force = false)
        {
            ThrowIfDisposed();

            if (duration < 0f)
                duration = _defaultDuration;

            if (_duplicateComparer != null && IsDuplicate(data))
                return false;

            if (_pending.Count >= _capacity)
            {
                if (!force)
                    return false;

                _pending.RemoveAt(_pending.Count - 1); // Cutting in — drop the latest pending entry.
            }

            if (force)
            {
                _pending.Insert(0, new Entry(data, duration));
                _skip?.TrySetResult(); // If showing, skip the hold time and go to hide.
            }
            else
            {
                _pending.Add(new Entry(data, duration));
            }

            if (!_draining)
                DrainAsync().Forget();

            return true;
        }

        /// <summary>Drops all pending entries. A showing toast finishes normally.</summary>
        public void Clear() => _pending.Clear();

        /// <summary>Clears pending + cancels in-flight + destroys the view. <see cref="Show"/> throws afterward.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _pending.Clear();
            _lifetime.Cancel();
            _lifetime.Dispose();

            if (_view)
                _view.gameObject.SafeDestroy();
        }

        private bool IsDuplicate(TData data)
        {
            if (_hasCurrent && _duplicateComparer.Equals(_currentData, data))
                return true;

            for (int i = 0; i < _pending.Count; i++)
            {
                if (_duplicateComparer.Equals(_pending[i].Data, data))
                    return true;
            }

            return false;
        }

        private async UniTask DrainAsync()
        {
            _draining = true;
            try
            {
                var token = _lifetime.Token;

                while (_pending.Count > 0 && !token.IsCancellationRequested)
                {
                    var entry = _pending[0];
                    _pending.RemoveAt(0);

                    var view = await GetViewAsync(token);
                    if (view == null)
                        break; // Factory failed/canceled — pending entries retry on the next Show.

                    _currentData = entry.Data;
                    _hasCurrent = true;
                    _skip = new UniTaskCompletionSource();

                    try
                    {
                        view.gameObject.SetActive(true);
                        view.OnData(entry.Data);
                        await view.PlayShowAsync(token);
                        await UniTask.WhenAny(view.WaitAsync(entry.Duration, token), _skip.Task);
                        await view.PlayHideAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Disposed — fall through to cleanup.
                    }
                    finally
                    {
                        _hasCurrent = false;
                        _currentData = default;
                        _skip = null;

                        if (view)
                            view.gameObject.SetActive(false);
                    }
                }
            }
            finally
            {
                _draining = false;
            }
        }

        private async UniTask<ToastView<TData>> GetViewAsync(CancellationToken cancellationToken)
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
                throw new ObjectDisposedException(nameof(ToastQueue<TData>));
        }
    }
}
