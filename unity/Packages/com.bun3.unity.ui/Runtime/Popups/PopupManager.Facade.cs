using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    // 자주 쓰는 스택 동작 위임 — PopupManager.Instance.Push(...) 한 단계로 쓰기 위한 파사드.
    // 전체 API(이벤트, Popups 뷰 등)는 Stack으로.
    public sealed partial class PopupManager
    {
        /// <summary>최상단 팝업. <see cref="PopupStack.Top"/> 위임.</summary>
        public Popup Top => Stack.Top;

        /// <summary>열려 있거나 전이 중인 팝업 수. <see cref="PopupStack.Count"/> 위임.</summary>
        public int Count => Stack.Count;

        /// <summary><see cref="PopupStack.IsOpen"/> 위임.</summary>
        public bool IsOpen(PopupKey key) => Stack.IsOpen(key);

        /// <summary>타입 키 <c>IsOpen&lt;TPopup&gt;</c> 위임.</summary>
        public bool IsOpen<TPopup>(string popupName = null) where TPopup : Popup
            => Stack.IsOpen<TPopup>(popupName);

        /// <summary><see cref="PopupStack.Push"/> 위임.</summary>
        public void Push(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
            => Stack.Push(key, layer, duplicate);

        /// <summary><see cref="PopupStack.PushWithArg{TArg}"/> 위임.</summary>
        public void PushWithArg<TArg>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
            => Stack.PushWithArg(key, arg, layer, duplicate);

        /// <summary><see cref="PopupStack.PushAsync"/> 위임.</summary>
        public UniTask<Popup> PushAsync(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
            => Stack.PushAsync(key, layer, duplicate);

        /// <summary><see cref="PopupStack.PushWithArgAsync{TArg}"/> 위임.</summary>
        public UniTask<Popup> PushWithArgAsync<TArg>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
            => Stack.PushWithArgAsync(key, arg, layer, duplicate);

        /// <summary><see cref="PopupStack.PushForResultAsync{TResult}"/> 위임.</summary>
        public UniTask<TResult> PushForResultAsync<TResult>(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore, TResult defaultResult = default)
            => Stack.PushForResultAsync(key, layer, duplicate, defaultResult);

        /// <summary><see cref="PopupStack.PushForResultAsync{TArg,TResult}"/> 위임.</summary>
        public UniTask<TResult> PushForResultAsync<TArg, TResult>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore, TResult defaultResult = default)
            => Stack.PushForResultAsync(key, arg, layer, duplicate, defaultResult);

        /// <summary>타입 키 <c>Push&lt;TPopup&gt;</c> 위임.</summary>
        public void Push<TPopup>(string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.Push<TPopup>(popupName, layer, duplicate);

        /// <summary>타입 키 <c>PushAsync&lt;TPopup&gt;</c> 위임 — 타입된 인스턴스 반환.</summary>
        public UniTask<TPopup> PushAsync<TPopup>(string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.PushAsync<TPopup>(popupName, layer, duplicate);

        /// <summary>타입 키 <c>PushWithArg&lt;TPopup, TArg&gt;</c> 위임.</summary>
        public void PushWithArg<TPopup, TArg>(TArg arg, string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.PushWithArg<TPopup, TArg>(arg, popupName, layer, duplicate);

        /// <summary>타입 키 <c>PushWithArgAsync&lt;TPopup, TArg&gt;</c> 위임.</summary>
        public UniTask<TPopup> PushWithArgAsync<TPopup, TArg>(TArg arg, string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.PushWithArgAsync<TPopup, TArg>(arg, popupName, layer, duplicate);

        /// <summary>구성 체인 <c>Push&lt;TPopup&gt;(configure)</c> 위임.</summary>
        public void Push<TPopup>(System.Action<TPopup> configure, string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.Push(configure, popupName, layer, duplicate);

        /// <summary>구성 체인 <c>PushAsync&lt;TPopup&gt;(configure)</c> 위임.</summary>
        public UniTask<TPopup> PushAsync<TPopup>(System.Action<TPopup> configure, string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Stack.PushAsync(configure, popupName, layer, duplicate);

        /// <summary>구성 체인 <c>PushForResultAsync&lt;TPopup, TResult&gt;(configure)</c> 위임.</summary>
        public UniTask<TResult> PushForResultAsync<TPopup, TResult>(System.Action<TPopup> configure,
            string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore,
            TResult defaultResult = default) where TPopup : Popup<TResult>
            => Stack.PushForResultAsync(configure, popupName, layer, duplicate, defaultResult);

        /// <summary>타입 키 <c>PushForResultAsync&lt;TPopup, TResult&gt;</c> 위임 — 팝업↔결과 타입 컴파일 검증.</summary>
        public UniTask<TResult> PushForResultAsync<TPopup, TResult>(string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore, TResult defaultResult = default)
            where TPopup : Popup<TResult>
            => Stack.PushForResultAsync<TPopup, TResult>(popupName, layer, duplicate, defaultResult);

        /// <summary>타입 키 + 초기 데이터 <c>PushForResultAsync&lt;TPopup, TArg, TResult&gt;</c> 위임.</summary>
        public UniTask<TResult> PushForResultAsync<TPopup, TArg, TResult>(TArg arg, string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore,
            TResult defaultResult = default) where TPopup : Popup<TResult>
            => Stack.PushForResultAsync<TPopup, TArg, TResult>(arg, popupName, layer, duplicate, defaultResult);

        /// <summary><see cref="PopupStack.Enqueue"/> 위임.</summary>
        public void Enqueue(PopupKey key, int layer = 0) => Stack.Enqueue(key, layer);

        /// <summary><see cref="PopupStack.EnqueueWithArg{TArg}"/> 위임.</summary>
        public void EnqueueWithArg<TArg>(PopupKey key, TArg arg, int layer = 0)
            => Stack.EnqueueWithArg(key, arg, layer);

        /// <summary>타입 키 <c>Enqueue&lt;TPopup&gt;</c> 위임(스택 대기열 — 화면 비면 순차).</summary>
        public void Enqueue<TPopup>(string popupName = null, int layer = 0) where TPopup : Popup
            => Stack.Enqueue(PopupKey.Of<TPopup>(popupName), layer);

        /// <summary>타입 키 <c>EnqueueWithArg&lt;TPopup, TArg&gt;</c> 위임.</summary>
        public void EnqueueWithArg<TPopup, TArg>(TArg arg, string popupName = null, int layer = 0)
            where TPopup : Popup
            => Stack.EnqueueWithArg(PopupKey.Of<TPopup>(popupName), arg, layer);

        /// <summary><see cref="PopupStack.CloseAll(Popup)"/> 위임.</summary>
        public void CloseAll(Popup except = null) => Stack.CloseAll(except);

        /// <summary><see cref="PopupStack.WaitUntilEmptyAsync"/> 위임.</summary>
        public UniTask WaitUntilEmptyAsync() => Stack.WaitUntilEmptyAsync();

        /// <summary><see cref="PopupStack.Pop"/> 위임.</summary>
        public void Pop() => Stack.Pop();

        /// <summary><see cref="PopupStack.Close"/> 위임.</summary>
        public void Close(Popup popup) => Stack.Close(popup);

        /// <summary><see cref="PopupStack.HandleBack"/> 위임.</summary>
        public bool HandleBack() => Stack.HandleBack();

        /// <summary><see cref="PopupStack.Clear"/> 위임.</summary>
        public void Clear() => Stack.Clear();
    }
}
