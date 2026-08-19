// PopupManager partial — stack delegation (facade).
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    // Facade delegating the common stack operations so PopupManager.Instance.Push(...) is one hop.
    // For the full API (events, Popups view, etc.) go through Stack.
    public sealed partial class PopupManager
    {
        /// <summary>Topmost popup. Delegates to <see cref="PopupStack.Top"/>.</summary>
        public Popup Top => Stack.Top;

        /// <summary>Number of popups open or in transition. Delegates to <see cref="PopupStack.Count"/>.</summary>
        public int Count => Stack.Count;

        /// <summary>Delegates to <see cref="PopupStack.IsOpen"/>.</summary>
        public bool IsOpen(PopupKey key) => Stack.IsOpen(key);

        /// <summary>Delegates to type-key <c>IsOpen&lt;TPopup&gt;</c>.</summary>
        public bool IsOpen<TPopup>(string popupName = null) where TPopup : Popup
            => Stack.IsOpen<TPopup>(popupName);

        /// <summary>Delegates to <see cref="PopupStack.Push"/>.</summary>
        public void Push(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
            => Stack.Push(key, layer, duplicate);

        /// <summary>Delegates to <see cref="PopupStack.PushWithArg{TArg}"/>.</summary>
        public void PushWithArg<TArg>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
            => Stack.PushWithArg(key, arg, layer, duplicate);

        /// <summary>Delegates to <see cref="PopupStack.PushAsync"/>.</summary>
        public UniTask<Popup> PushAsync(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
            => Stack.PushAsync(key, layer, duplicate);

        /// <summary>Delegates to <see cref="PopupStack.PushWithArgAsync{TArg}"/>.</summary>
        public UniTask<Popup> PushWithArgAsync<TArg>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
            => Stack.PushWithArgAsync(key, arg, layer, duplicate);

        /// <summary>Delegates to <see cref="PopupStack.PushForResultAsync{TResult}"/>.</summary>
        public UniTask<TResult> PushForResultAsync<TResult>(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore, TResult defaultResult = default)
            => Stack.PushForResultAsync(key, layer, duplicate, defaultResult);

        /// <summary>Delegates to <see cref="PopupStack.PushForResultAsync{TArg,TResult}"/>.</summary>
        public UniTask<TResult> PushForResultAsync<TArg, TResult>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore, TResult defaultResult = default)
            => Stack.PushForResultAsync(key, arg, layer, duplicate, defaultResult);

        /// <summary>Delegates to type-key <c>Push&lt;TPopup&gt;</c>.</summary>
        public void Push<TPopup>(string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.Push<TPopup>(popupName, layer, duplicate);

        /// <summary>Delegates to type-key <c>PushAsync&lt;TPopup&gt;</c> — returns the typed instance.</summary>
        public UniTask<TPopup> PushAsync<TPopup>(string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.PushAsync<TPopup>(popupName, layer, duplicate);

        /// <summary>Delegates to type-key <c>PushWithArg&lt;TPopup, TArg&gt;</c>.</summary>
        public void PushWithArg<TPopup, TArg>(TArg arg, string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.PushWithArg<TPopup, TArg>(arg, popupName, layer, duplicate);

        /// <summary>Delegates to type-key <c>PushWithArgAsync&lt;TPopup, TArg&gt;</c>.</summary>
        public UniTask<TPopup> PushWithArgAsync<TPopup, TArg>(TArg arg, string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.PushWithArgAsync<TPopup, TArg>(arg, popupName, layer, duplicate);

        /// <summary>Delegates to configure-chain <c>Push&lt;TPopup&gt;(configure)</c>.</summary>
        public void Push<TPopup>(System.Action<TPopup> configure, string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.Push(configure, popupName, layer, duplicate);

        /// <summary>Delegates to configure-chain <c>PushAsync&lt;TPopup&gt;(configure)</c>.</summary>
        public UniTask<TPopup> PushAsync<TPopup>(System.Action<TPopup> configure, string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.PushAsync(configure, popupName, layer, duplicate);

        /// <summary>Delegates to configure-chain <c>PushForResultAsync&lt;TPopup, TResult&gt;(configure)</c>.</summary>
        public UniTask<TResult> PushForResultAsync<TPopup, TResult>(System.Action<TPopup> configure,
            string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore,
            TResult defaultResult = default) where TPopup : Popup<TResult>
            => Stack.PushForResultAsync(configure, popupName, layer, duplicate, defaultResult);

        /// <summary>Delegates to type-key <c>PushForResultAsync&lt;TPopup, TResult&gt;</c> — popup/result types checked at compile time.</summary>
        public UniTask<TResult> PushForResultAsync<TPopup, TResult>(string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore, TResult defaultResult = default)
            where TPopup : Popup<TResult>
            => Stack.PushForResultAsync<TPopup, TResult>(popupName, layer, duplicate, defaultResult);

        /// <summary>Delegates to type-key + initial-data <c>PushForResultAsync&lt;TPopup, TArg, TResult&gt;</c>.</summary>
        public UniTask<TResult> PushForResultAsync<TPopup, TArg, TResult>(TArg arg, string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore,
            TResult defaultResult = default) where TPopup : Popup<TResult>
            => Stack.PushForResultAsync<TPopup, TArg, TResult>(arg, popupName, layer, duplicate, defaultResult);

        /// <summary>Delegates to <see cref="PopupStack.Enqueue"/>.</summary>
        public void Enqueue(PopupKey key, int layer = 0) => Stack.Enqueue(key, layer);

        /// <summary>Delegates to <see cref="PopupStack.EnqueueWithArg{TArg}"/>.</summary>
        public void EnqueueWithArg<TArg>(PopupKey key, TArg arg, int layer = 0)
            => Stack.EnqueueWithArg(key, arg, layer);

        /// <summary>Delegates to type-key <c>Enqueue&lt;TPopup&gt;</c> (stack queue — shows sequentially when the screen is empty).</summary>
        public void Enqueue<TPopup>(string popupName = null, int layer = 0) where TPopup : Popup
            => Stack.Enqueue(PopupKey.Of<TPopup>(popupName), layer);

        /// <summary>Delegates to type-key <c>EnqueueWithArg&lt;TPopup, TArg&gt;</c>.</summary>
        public void EnqueueWithArg<TPopup, TArg>(TArg arg, string popupName = null, int layer = 0)
            where TPopup : Popup
            => Stack.EnqueueWithArg(PopupKey.Of<TPopup>(popupName), arg, layer);

        /// <summary>Delegates to <see cref="PopupStack.CloseAll(Popup)"/>.</summary>
        public void CloseAll(Popup except = null) => Stack.CloseAll(except);

        /// <summary>Delegates to <see cref="PopupStack.WaitUntilEmptyAsync"/>.</summary>
        public UniTask WaitUntilEmptyAsync() => Stack.WaitUntilEmptyAsync();

        /// <summary>Delegates to <see cref="PopupStack.Pop"/>.</summary>
        public void Pop() => Stack.Pop();

        /// <summary>Delegates to <see cref="PopupStack.Close"/>.</summary>
        public void Close(Popup popup) => Stack.Close(popup);

        /// <summary>Delegates to <see cref="PopupStack.HandleBack"/>.</summary>
        public bool HandleBack() => Stack.HandleBack();

        /// <summary>Delegates to <see cref="PopupStack.Clear"/>.</summary>
        public void Clear() => Stack.Clear();
    }
}
