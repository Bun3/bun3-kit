// PopupStack partial — result awaiting.
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    // Result path: Push variants that open a Popup<TResult> and await its close result.
    public sealed partial class PopupStack
    {
        /// <summary>
        /// Opens a result popup (<see cref="Popup{TResult}"/>), waits until it closes, and returns
        /// the result. Closed without <c>SetResult</c> (back/cancel) yields
        /// <paramref name="defaultResult"/>, as does not opening (duplicate policy) or cancellation.
        /// </summary>
        public async UniTask<TResult> PushForResultAsync<TResult>(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore, TResult defaultResult = default)
            => await WaitForResultCore(await PushAsync(key, layer, duplicate), defaultResult);

        /// <summary>
        /// Opens a result popup with initial data and awaits the result.
        /// No partial generic inference — specify both types at the call site:
        /// <c>PushForResultAsync&lt;string, bool&gt;(key, "Are you sure?")</c>.
        /// </summary>
        public async UniTask<TResult> PushForResultAsync<TArg, TResult>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore, TResult defaultResult = default)
            => await WaitForResultCore(await PushWithArgAsync(key, arg, layer, duplicate), defaultResult);

        /// <summary>
        /// Opens a result popup by type key and awaits the result. The
        /// <c>where TPopup : Popup&lt;TResult&gt;</c> constraint makes a popup/result type
        /// mismatch a <b>compile error</b>: <c>PushForResultAsync&lt;ConfirmPopup, bool&gt;()</c>.
        /// </summary>
        public async UniTask<TResult> PushForResultAsync<TPopup, TResult>(string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore,
            TResult defaultResult = default) where TPopup : Popup<TResult>
            => await WaitForResultCore(
                await PushAsync(PopupKey.Of<TPopup>(popupName), layer, duplicate), defaultResult);

        /// <summary>Opens a result popup by type key with initial data. Specify all three types.</summary>
        public async UniTask<TResult> PushForResultAsync<TPopup, TArg, TResult>(TArg arg,
            string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore,
            TResult defaultResult = default) where TPopup : Popup<TResult>
            => await WaitForResultCore(
                await PushWithArgAsync(PopupKey.Of<TPopup>(popupName), arg, layer, duplicate), defaultResult);

        private static async UniTask<TResult> WaitForResultCore<TResult>(Popup popup, TResult defaultResult)
        {
            if (popup == null)
                return defaultResult;

            if (popup is Popup<TResult> typed)
                return await typed.WaitForResultAsync(defaultResult);

            // Game wiring error — low-frequency path, string allocation OK.
            Debug.LogError(
                $"Popup {popup.GetType().Name} is not a Popup<{typeof(TResult).Name}>; cannot receive a result.",
                popup);
            return defaultResult;
        }
    }
}
