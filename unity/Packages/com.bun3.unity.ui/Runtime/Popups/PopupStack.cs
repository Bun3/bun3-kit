using System;
using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Domain-agnostic popup/modal stack. Handles push/pop, layer sorting, duplicate policy,
    /// sequential queue, back-key routing, and initial-data delivery. Instance creation/release
    /// and presentation (parenting, dim, sound) are supplied by the game via
    /// <see cref="PopupFactory"/>/<see cref="PopupReleaser"/>/<see cref="Opened"/> hooks.
    /// </summary>
    /// <remarks>
    /// Not a MonoBehaviour — imposes no scene structure; the game creates and holds it.
    /// The push/pop/back paths allocate no closures, LINQ, or strings. Attach a
    /// <see cref="PopupBackKeyRouter"/> for automatic back-key routing.
    /// <br/>
    /// Partial layout: this file (state/lifetime/sorting) / Push (open, duplicate policy) /
    /// Close (close, back) / Result (result await) / Queue (sequential queue).
    /// </remarks>
    public sealed partial class PopupStack : IDisposable
    {
        private readonly PopupFactory _factory;
        private readonly PopupReleaser _releaser;

        // Sort invariant: (Layer ascending, insertion order). End = topmost.
        private readonly List<Popup> _stack = new();
        private readonly List<PopupKey> _loading = new();

        private CancellationTokenSource _lifetime = new();
        private bool _disposed;

        /// <summary>Fired right after a popup is inserted into the stack (before the open transition). Hook point for z-order/dim presentation.</summary>
        public event Action<Popup> Opened;

        /// <summary>Fired right after a popup is removed from the stack (just before release).</summary>
        public event Action<Popup> Closed;

        /// <summary>Fired when <see cref="PopupDuplicatePolicy.Focus"/> reuses an existing instance at the top.</summary>
        public event Action<Popup> Focused;

        /// <summary>
        /// Fired the moment the stack, loading set, and sequential queue are all empty.
        /// Signal for flows that must start only when no popup exists (auto-landing, tutorials).
        /// </summary>
        public event Action Emptied;

        private UniTaskCompletionSource _emptySource;

        /// <summary>Whether open popups, in-flight loads, and the sequential queue are all empty.</summary>
        public bool IsEmpty => _stack.Count == 0 && _loading.Count == 0 && _queue.Count == 0;

        /// <summary>Waits until everything is empty. Completes immediately if already empty.</summary>
        public UniTask WaitUntilEmptyAsync()
        {
            if (IsEmpty)
                return UniTask.CompletedTask;

            _emptySource ??= new UniTaskCompletionSource();
            return _emptySource.Task;
        }

        private void NotifyIfEmpty()
        {
            if (!IsEmpty)
                return;

            var source = _emptySource;
            _emptySource = null;
            source?.TrySetResult();
            Emptied?.Invoke();
        }

        /// <summary>Number of popups open or in transition.</summary>
        public int Count => _stack.Count;

        /// <summary>
        /// Read-only view of open popups, ordered bottom to top (end = topmost). This is a live
        /// view — do not call Push/Close while enumerating; copy if a snapshot is needed.
        /// </summary>
        public IReadOnlyList<Popup> Popups => _stack;

        /// <summary>Topmost popup, or null when empty.</summary>
        public Popup Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        /// <param name="factory">Key to popup instance. Loading strategy is the game's choice.</param>
        /// <param name="releaser">Releases closed instances. Default destroys the GameObject.</param>
        public PopupStack(PopupFactory factory, PopupReleaser releaser = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _releaser = releaser ?? DestroyPopup;
        }

        /// <summary>Whether a popup with the key is open (excluding ones in the closing transition).</summary>
        public bool IsOpen(PopupKey key)
        {
            for (int i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].Key == key && _stack[i].Phase != PopupPhase.Closing)
                    return true;
            }

            return false;
        }

        /// <summary>Whether a popup with the type key is open.</summary>
        public bool IsOpen<TPopup>(string popupName = null) where TPopup : Popup
            => IsOpen(PopupKey.Of<TPopup>(popupName));

        /// <summary>
        /// Releases everything immediately, skipping transitions. In-flight loads/transitions are
        /// canceled, the sequential queue is emptied, and close locks are ignored. For forced
        /// cleanup such as scene changes.
        /// </summary>
        public void Clear()
        {
            var lifetime = _lifetime;
            _lifetime = new CancellationTokenSource();
            lifetime.Cancel();
            lifetime.Dispose();

            _queue.Clear();
            // _loading is not cleared — each in-flight load's finally removes its own entry.
            // The token is canceled, so arriving instances are released instead of entering the stack.

            if (_stack.Count == 0)
            {
                NotifyIfEmpty();
                return;
            }

            // Release callbacks may re-Push, so snapshot first. (Low-frequency path — allocation OK.)
            var popups = _stack.ToArray();
            _stack.Clear();

            for (int i = popups.Length - 1; i >= 0; i--)
            {
                var popup = popups[i];
                popup.Detach();
                Closed?.Invoke(popup);
                _releaser(popup);
            }

            NotifyIfEmpty();
        }

        /// <summary>Runs <see cref="Clear"/> and makes the stack unusable.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Clear();
            _disposed = true;
            _lifetime.Dispose();
        }

        /// <summary>
        /// Notifies all popups of their order after a structural change (open/close/Focus)
        /// and refreshes dims. Dim rule: only the topmost popup with a
        /// <see cref="Popup.BackgroundDim"/> is dimmed — a dimless popup on top keeps
        /// the dim of the popup below it.
        /// </summary>
        private void NotifyStackOrderChanged()
        {
            Popup dimOwner = null;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].BackgroundDim)
                {
                    dimOwner = _stack[i];
                    break;
                }
            }

            var top = Top;
            for (int i = 0; i < _stack.Count; i++)
            {
                var popup = _stack[i];

                if (popup.BackgroundDim)
                    popup.BackgroundDim.SetActive(ReferenceEquals(popup, dimOwner));

                popup.UpdateTopmost(ReferenceEquals(popup, top));
                popup.OnStackOrderChanged(i, ReferenceEquals(popup, top));
            }
        }

        private void InsertSorted(Popup popup, int layer)
        {
            int index = _stack.Count;
            while (index > 0 && _stack[index - 1].Layer > layer)
                index--;

            _stack.Insert(index, popup);
        }

        private Popup FindTopmostOpen(PopupKey key)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Key == key && _stack[i].Phase != PopupPhase.Closing)
                    return _stack[i];
            }

            return null;
        }

        private bool IsLoading(PopupKey key)
        {
            for (int i = 0; i < _loading.Count; i++)
            {
                if (_loading[i] == key)
                    return true;
            }

            return false;
        }

        internal static void DeliverArg<TArg>(Popup popup, TArg arg)
        {
            if (popup is IPopupArg<TArg> receiver)
                receiver.OnPopupArg(arg);
            else
                // Game wiring error — low-frequency path, string allocation OK.
                Debug.LogError(
                    $"Popup {popup.GetType().Name} does not implement IPopupArg<{typeof(TArg).Name}>; dropping the initial data.",
                    popup);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PopupStack));
        }

        private static void DestroyPopup(Popup popup)
        {
            if (popup)
                popup.gameObject.SafeDestroy();
        }
    }
}
