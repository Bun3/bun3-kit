// PopupStack partial — close and back handling.
using System;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    // Close path: Close/Pop and back-key routing.
    public sealed partial class PopupStack
    {
        /// <summary>
        /// Routes the back key (ESC/Android back) to the topmost popup.
        /// </summary>
        /// <returns>
        /// True if the key was consumed. False only when the stack is empty — the game continues
        /// with its own handling (e.g. quit confirmation). If the topmost popup is in transition
        /// or close-locked, nothing happens but the key is consumed; if
        /// <see cref="Popup.OnBackRequested"/> returns false, the key is consumed without closing.
        /// </returns>
        public bool HandleBack()
        {
            var top = Top;
            if (top == null)
                return false;

            if (top.Phase != PopupPhase.Open || top.IsCloseBlocked)
                return true;

            if (!top.OnBackRequested())
                return true;

            Close(top);
            return true;
        }

        /// <summary>Closes the topmost popup that is not already closing.</summary>
        public void Pop()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Phase != PopupPhase.Closing)
                {
                    Close(_stack[i]);
                    return;
                }
            }
        }

        /// <summary>Closes a popup. Fire-and-forget: does not wait for the close transition.</summary>
        public void Close(Popup popup) => CloseAsync(popup).Forget();

        /// <summary>
        /// Closes a popup and waits for the close transition and release. Ignored when the popup
        /// belongs to another stack or is already closing. If it is still opening or close-locked
        /// (<see cref="Popup.IsCloseBlocked"/>), the close is only deferred and this returns
        /// immediately — it closes automatically when the open completes / the last lock releases.
        /// Use <see cref="Popup.WaitUntilClosedAsync"/> to wait for the actual close.
        /// </summary>
        public async UniTask CloseAsync(Popup popup)
        {
            if (popup == null || popup.Stack != this || popup.Phase == PopupPhase.Closing)
                return;

            if (popup.Phase == PopupPhase.Opening || popup.IsCloseBlocked)
            {
                popup.CloseRequested = true;
                return;
            }

            popup.SetPhase(PopupPhase.Closing);
            popup.OnTransitionStarted();

            try
            {
                await popup.PlayHideAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // Clear/Dispose owns the release.
            }

            if (popup.Stack != this || popup.Phase != PopupPhase.Closing)
                return;

            _stack.Remove(popup);
            popup.Detach();
            Closed?.Invoke(popup);
            NotifyStackOrderChanged();
            _releaser(popup);

            TryDrainQueue();
            NotifyIfEmpty();
        }

        /// <summary>
        /// Closes all open popups (optionally keeping <paramref name="except"/>) via the normal
        /// path — close transitions, hooks, and events all run (use <see cref="Clear"/> for
        /// forced cleanup that skips transitions).
        /// </summary>
        public void CloseAll(Popup except = null)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var popup = _stack[i];
                if (popup.Phase != PopupPhase.Closing && !ReferenceEquals(popup, except))
                    Close(popup);
            }
        }

        /// <summary>Closes only matching popups via the normal path. (Low-frequency path — delegate OK.)</summary>
        public void CloseAll(Predicate<Popup> match)
        {
            if (match == null)
                throw new ArgumentNullException(nameof(match));

            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var popup = _stack[i];
                if (popup.Phase != PopupPhase.Closing && match(popup))
                    Close(popup);
            }
        }

        private void CloseAllOf(PopupKey key)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var popup = _stack[i];
                if (popup.Key == key && popup.Phase != PopupPhase.Closing)
                    Close(popup);
            }
        }
    }
}
