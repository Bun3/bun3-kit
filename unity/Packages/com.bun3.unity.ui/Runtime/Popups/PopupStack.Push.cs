// PopupStack partial — open (push) and duplicate policy.
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    // Open path: Push family (raw key/type key), duplicate-policy resolution, actual open sequence (OpenAsync).
    public sealed partial class PopupStack
    {
        private enum ArgMode : byte
        {
            None,
            Typed,
            Queued,
        }

        private enum DuplicateDecision : byte
        {
            Proceed,
            Drop,
            Enqueue,
            Focus,
        }

        /// <summary>
        /// Opens a popup. Fire-and-forget: does not wait for loading or the open transition.
        /// Factory exceptions surface via the UniTask unobserved-exception handler.
        /// </summary>
        public void Push(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
        {
            ThrowIfDisposed();
            PushAsync(key, layer, duplicate).Forget();
        }

        /// <summary>
        /// Opens with initial data. The popup must implement <see cref="IPopupArg{TArg}"/>.
        /// (Named separately so the x in <c>Push(key, x)</c> can never be mistaken for layer —
        /// int data is carried safely.)
        /// </summary>
        public void PushWithArg<TArg>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
        {
            ThrowIfDisposed();
            PushWithArgAsync(key, arg, layer, duplicate).Forget();
        }

        /// <summary>
        /// Opens a popup, waits for the open transition, and returns the instance.
        /// If the same key is already open or loading, <paramref name="duplicate"/> applies.
        /// </summary>
        /// <returns>
        /// The opened instance, or null if dropped/queued by the duplicate policy, the factory
        /// returned null, or a <see cref="Clear"/> canceled it mid-flight. (A close deferred during
        /// opening may have already closed it — check <see cref="Popup.Phase"/> before further use.)
        /// </returns>
        public async UniTask<Popup> PushAsync(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
        {
            ThrowIfDisposed();

            switch (ResolveDuplicate(key, duplicate))
            {
                case DuplicateDecision.Drop:
                    return null;
                case DuplicateDecision.Enqueue:
                    Enqueue(key, layer);
                    return null;
                case DuplicateDecision.Focus:
                    return FocusExisting(key, (byte)0, false);
            }

            return await OpenAsync(key, layer, (byte)0, ArgMode.None);
        }

        /// <summary>
        /// Opens with initial data, waits for the open transition, and returns the instance.
        /// The data is delivered via <see cref="IPopupArg{TArg}.OnPopupArg"/> right after the
        /// factory load, before the open transition — no need to create the instance
        /// synchronously just to initialize it.
        /// </summary>
        /// <returns>Same as <see cref="PushAsync(PopupKey,int,PopupDuplicatePolicy)"/>.</returns>
        public async UniTask<Popup> PushWithArgAsync<TArg>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
        {
            ThrowIfDisposed();

            switch (ResolveDuplicate(key, duplicate))
            {
                case DuplicateDecision.Drop:
                    return null;
                case DuplicateDecision.Enqueue:
                    EnqueueWithArg(key, arg, layer);
                    return null;
                case DuplicateDecision.Focus:
                    return FocusExisting(key, arg, true);
            }

            return await OpenAsync(key, layer, arg, ArgMode.Typed);
        }

        // ── Type = key (default convention) ──
        // popupName is for variants that open different prefabs with the same class — null uses the class name as the key.

        /// <summary>Opens using the type as the key. Fire-and-forget.</summary>
        public void Push<TPopup>(string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Push(PopupKey.Of<TPopup>(popupName), layer, duplicate);

        /// <summary>Opens using the type as the key and returns the <b>typed instance</b> after the open completes (no casting).</summary>
        public async UniTask<TPopup> PushAsync<TPopup>(string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => CastOrNull<TPopup>(await PushAsync(PopupKey.Of<TPopup>(popupName), layer, duplicate));

        /// <summary>Opens using the type as the key, with initial data. Fire-and-forget.</summary>
        public void PushWithArg<TPopup, TArg>(TArg arg, string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => PushWithArg(PopupKey.Of<TPopup>(popupName), arg, layer, duplicate);

        /// <summary>Opens using the type as the key, with initial data, and returns the typed instance.</summary>
        public async UniTask<TPopup> PushWithArgAsync<TPopup, TArg>(TArg arg, string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => CastOrNull<TPopup>(await PushWithArgAsync(PopupKey.Of<TPopup>(popupName), arg, layer, duplicate));

        // ── configure channel ──
        // Runs the game popup's fluent setter chain "after async loading, before the open transition" —
        // keeps the Show().SetTitle().SetDesc() DX without creating the instance synchronously.
        // (Low-frequency dialog path — closure/carrier allocation OK.)

        /// <summary>Opens with a configure chain. Fire-and-forget.</summary>
        public void Push<TPopup>(Action<TPopup> configure, string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
        {
            ThrowIfDisposed();
            PushAsync(configure, popupName, layer, duplicate).Forget();
        }

        /// <summary>
        /// Opens with a configure chain and returns the typed instance. <paramref name="configure"/>
        /// runs right after loading, before the open transition. With
        /// <see cref="PopupDuplicatePolicy.Focus"/> it is re-applied to the existing instance;
        /// with <see cref="PopupDuplicatePolicy.Queue"/> it is held until display time.
        /// </summary>
        public async UniTask<TPopup> PushAsync<TPopup>(Action<TPopup> configure, string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
        {
            ThrowIfDisposed();

            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            var key = PopupKey.Of<TPopup>(popupName);
            var carrier = new PopupConfigureArg<TPopup>(configure);

            switch (ResolveDuplicate(key, duplicate))
            {
                case DuplicateDecision.Drop:
                    return null;
                case DuplicateDecision.Enqueue:
                    EnqueueCore(key, layer, carrier);
                    return null;
                case DuplicateDecision.Focus:
                    return CastOrNull<TPopup>(FocusExisting(key, carrier));
            }

            return CastOrNull<TPopup>(await OpenAsync(key, layer, (IQueuedPopupArg)carrier, ArgMode.Queued));
        }

        /// <summary>
        /// Opens a result popup with a configure chain and waits for the result:
        /// <c>await PushForResultAsync&lt;AlertPopup, bool&gt;(p =&gt; p.SetTitle("...").SetDesc("..."))</c>.
        /// </summary>
        public async UniTask<TResult> PushForResultAsync<TPopup, TResult>(Action<TPopup> configure,
            string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore,
            TResult defaultResult = default) where TPopup : Popup<TResult>
        {
            var popup = await PushAsync(configure, popupName, layer, duplicate);
            return popup == null
                ? defaultResult
                : await popup.WaitForResultAsync(defaultResult);
        }

        private Popup FocusExisting(PopupKey key, IQueuedPopupArg carrier)
        {
            var popup = FindTopmostOpen(key);
            if (popup == null)
            {
                Debug.LogWarning($"Focus target popup ({key.Name}) is still loading; dropping the configure chain.");
                return null;
            }

            carrier.Deliver(popup);
            _stack.Remove(popup);
            InsertSorted(popup, popup.Layer);
            Focused?.Invoke(popup);
            NotifyStackOrderChanged();
            return popup;
        }

        private DuplicateDecision ResolveDuplicate(PopupKey key, PopupDuplicatePolicy duplicate)
        {
            if (!IsOpen(key) && !IsLoading(key))
                return DuplicateDecision.Proceed;

            switch (duplicate)
            {
                case PopupDuplicatePolicy.Ignore:
                    return DuplicateDecision.Drop;

                case PopupDuplicatePolicy.Queue:
                    return DuplicateDecision.Enqueue;

                case PopupDuplicatePolicy.Focus:
                    return DuplicateDecision.Focus;

                default:
                    // Replace. ponytail: same-key instances still loading are left alone
                    // (concurrent loads allowed). Extend to load cancellation if needed.
                    CloseAllOf(key);
                    return DuplicateDecision.Proceed;
            }
        }

        private Popup FocusExisting<TArg>(PopupKey key, TArg arg, bool hasArg)
        {
            var popup = FindTopmostOpen(key);
            if (popup == null)
            {
                // Only a loading instance exists — nothing to touch. Surface the loss if an arg was carried.
                if (hasArg)
                    Debug.LogWarning($"Focus target popup ({key.Name}) is still loading; dropping the arg.");
                return null;
            }

            if (hasArg)
                DeliverArg(popup, arg);

            _stack.Remove(popup);
            InsertSorted(popup, popup.Layer); // To the top of its layer.
            Focused?.Invoke(popup);
            NotifyStackOrderChanged();
            return popup;
        }

        private async UniTask<Popup> OpenAsync<TArg>(PopupKey key, int layer, TArg arg, ArgMode argMode)
        {
            var token = _lifetime.Token;

            Popup popup;
            bool loaded = false;
            _loading.Add(key);
            try
            {
                popup = await _factory(key, token);
                loaded = true;
            }
            finally
            {
                _loading.Remove(key);
                // Keep the queue draining even when the factory throws.
                if (!loaded)
                {
                    TryDrainQueue();
                    NotifyIfEmpty();
                }
            }

            // The factory may signal load failure with null.
            if (popup == null)
            {
                TryDrainQueue();
                NotifyIfEmpty();
                return null;
            }

            if (token.IsCancellationRequested)
            {
                // Instance arrived after Clear/Dispose — release it without entering the stack.
                _releaser(popup);
                NotifyIfEmpty();
                return null;
            }

            if (argMode != ArgMode.None)
            {
                try
                {
                    if (argMode == ArgMode.Typed)
                        DeliverArg(popup, arg);
                    else
                        ((IQueuedPopupArg)(object)arg).Deliver(popup);
                }
                catch
                {
                    // If OnPopupArg throws, release the instance so it does not leak outside the stack, then surface.
                    _releaser(popup);
                    TryDrainQueue();
                    throw;
                }
            }

            InsertSorted(popup, layer);
            popup.Attach(this, key, layer);
            popup.OnTransitionStarted();
            Opened?.Invoke(popup);
            NotifyStackOrderChanged();

            try
            {
                await popup.PlayShowAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Clear/Dispose owns the release.
            }

            if (popup.Stack != this || popup.Phase != PopupPhase.Opening)
                return null; // Cleared during the open transition — never hand out a released instance.

            popup.SetPhase(PopupPhase.Open);
            popup.OnOpenCompleted(token);

            if (popup.CloseRequested && !popup.IsCloseBlocked)
                await CloseAsync(popup);

            return popup;
        }

        /// <summary>Carrier that holds a configure chain until display time. (Low-frequency — allocation OK.)</summary>
        private sealed class PopupConfigureArg<TPopup> : IQueuedPopupArg where TPopup : Popup
        {
            private readonly Action<TPopup> _configure;

            public PopupConfigureArg(Action<TPopup> configure) => _configure = configure;

            public void Deliver(Popup popup)
            {
                if (popup is TPopup typed)
                    _configure(typed);
                else
                    Debug.LogError(
                        $"Instance for key {popup.Key.Name} is {popup.GetType().Name}; cannot apply the {typeof(TPopup).Name} configure chain.",
                        popup);
            }
        }

        private static TPopup CastOrNull<TPopup>(Popup popup) where TPopup : Popup
        {
            if (popup == null)
                return null;

            if (popup is TPopup typed)
                return typed;

            // Game wiring error (key name vs. prefab type mismatch) — low-frequency path, string allocation OK.
            Debug.LogError(
                $"Instance for key {popup.Key.Name} is {popup.GetType().Name}; cannot open as {typeof(TPopup).Name}.",
                popup);
            return null;
        }
    }
}
