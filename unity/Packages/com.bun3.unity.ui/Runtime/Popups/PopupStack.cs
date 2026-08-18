using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 도메인 무관 팝업/모달 스택. push/pop, 레이어 정렬, 중복 정책, 순차 대기열,
    /// back 키 라우팅, 초기 데이터 전달을 담당한다. 팝업 인스턴스 생성/해제와 표현(부모 배치,
    /// 딤, 사운드)은 <see cref="PopupFactory"/>/<see cref="PopupReleaser"/>/<see cref="Opened"/>
    /// 훅으로 게임이 채운다.
    /// </summary>
    /// <remarks>
    /// MonoBehaviour가 아니다 — 씬 구조를 강제하지 않고 게임이 생성해 보관한다.
    /// push/pop/back 경로는 클로저·LINQ·문자열 할당이 없다. 필요하면
    /// <see cref="PopupBackKeyRouter"/>를 붙여 back 키를 자동 라우팅한다.
    /// </remarks>
    public sealed class PopupStack : IDisposable
    {
        private readonly struct QueuedPopup
        {
            public readonly PopupKey Key;
            public readonly int Layer;
            public readonly IQueuedPopupArg Arg;

            public QueuedPopup(PopupKey key, int layer, IQueuedPopupArg arg)
            {
                Key = key;
                Layer = layer;
                Arg = arg;
            }
        }

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

        private readonly PopupFactory _factory;
        private readonly PopupReleaser _releaser;

        // 정렬 불변식: (Layer 오름차순, 삽입 순서). 끝 = 최상단.
        private readonly List<Popup> _stack = new();
        private readonly Queue<QueuedPopup> _queue = new();
        private readonly List<PopupKey> _loading = new();

        private CancellationTokenSource _lifetime = new();
        private bool _disposed;

        /// <summary>팝업이 스택에 삽입된 직후(열림 연출 시작 전) 발화. z-order/딤 연출 연결 지점.</summary>
        public event Action<Popup> Opened;

        /// <summary>팝업이 스택에서 제거된 직후(해제 직전) 발화.</summary>
        public event Action<Popup> Closed;

        /// <summary><see cref="PopupDuplicatePolicy.Focus"/>로 기존 인스턴스가 최상단에 재사용될 때 발화.</summary>
        public event Action<Popup> Focused;

        /// <summary>열려 있거나 전이 중인 팝업 수.</summary>
        public int Count => _stack.Count;

        /// <summary>
        /// 열린 팝업들의 읽기 전용 뷰. 순서 = 아래→위(끝이 최상단). 라이브 뷰이므로
        /// 열거 중 Push/Close를 부르지 말 것 — 스냅샷이 필요하면 복사해서 쓴다.
        /// </summary>
        public IReadOnlyList<Popup> Popups => _stack;

        /// <summary>최상단 팝업. 비어 있으면 null.</summary>
        public Popup Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        /// <summary>순차 대기열에서 표시를 기다리는 항목 수.</summary>
        public int QueuedCount => _queue.Count;

        /// <param name="factory">키 → 팝업 인스턴스. 로딩 방식은 게임 몫.</param>
        /// <param name="releaser">닫힌 인스턴스 해제. 기본은 GameObject 파괴.</param>
        public PopupStack(PopupFactory factory, PopupReleaser releaser = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _releaser = releaser ?? DestroyPopup;
        }

        /// <summary>해당 키의 팝업이 열려 있는지(닫힘 연출 중 제외) 확인한다.</summary>
        public bool IsOpen(PopupKey key)
        {
            for (int i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].Key == key && _stack[i].Phase != PopupPhase.Closing)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 팝업을 연다. 로딩/열림 연출 완료를 기다리지 않는 fire-and-forget 버전.
        /// 팩토리 예외는 UniTask 미관찰 예외 핸들러로 표면화된다.
        /// </summary>
        public void Push(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
        {
            ThrowIfDisposed();
            PushAsync(key, layer, duplicate).Forget();
        }

        /// <summary>
        /// 초기 데이터를 실어 연다. 팝업은 <see cref="IPopupArg{TArg}"/>를 구현해야 한다.
        /// (<c>Push(key, x)</c>의 x가 layer로 해석되는 사고를 막으려고 이름을 분리했다 —
        /// int 데이터도 안전하게 싣는다.)
        /// </summary>
        public void PushWithArg<TArg>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore)
        {
            ThrowIfDisposed();
            PushWithArgAsync(key, arg, layer, duplicate).Forget();
        }

        /// <summary>
        /// 팝업을 열고 열림 연출 완료까지 대기한 뒤 인스턴스를 돌려준다.
        /// 같은 키가 이미 열려 있거나 로딩 중이면 <paramref name="duplicate"/> 정책을 따른다.
        /// </summary>
        /// <returns>
        /// 열린 인스턴스. 중복 정책으로 무시/큐잉됐거나, 팩토리가 null을 돌려줬거나,
        /// 진행 중 <see cref="Clear"/>로 취소됐으면 null. (열림 직후 예약된 닫기로 이미 닫혔을 수도
        /// 있으니, 반환 후 계속 쓸 거라면 <see cref="Popup.Phase"/>를 확인할 것.)
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
        /// 초기 데이터를 실어 열고 열림 연출 완료까지 대기한 뒤 인스턴스를 돌려준다.
        /// 데이터는 팩토리 로딩이 끝난 직후, 열림 연출 전에
        /// <see cref="IPopupArg{TArg}.OnPopupArg"/>로 전달된다 — 레거시처럼 인스턴스를
        /// 동기 생성해서 초기화할 필요가 없다.
        /// </summary>
        /// <returns><see cref="PushAsync(PopupKey,int,PopupDuplicatePolicy)"/>와 동일.</returns>
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

        /// <summary>
        /// 결과 팝업(<see cref="Popup{TResult}"/>)을 열고, 닫힐 때까지 기다린 뒤 결과를 돌려준다.
        /// <c>SetResult</c> 없이 닫히면(back/취소) <paramref name="defaultResult"/>.
        /// 중복 정책으로 열리지 않았거나 취소됐을 때도 <paramref name="defaultResult"/>.
        /// </summary>
        public async UniTask<TResult> PushForResultAsync<TResult>(PopupKey key, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore, TResult defaultResult = default)
            => await WaitForResultCore(await PushAsync(key, layer, duplicate), defaultResult);

        /// <summary>
        /// 초기 데이터를 실어 결과 팝업을 열고 결과까지 대기한다.
        /// 제네릭 부분 추론이 없어 호출 시 두 타입을 명시한다:
        /// <c>PushForResultAsync&lt;string, bool&gt;(key, "정말?")</c>.
        /// </summary>
        public async UniTask<TResult> PushForResultAsync<TArg, TResult>(PopupKey key, TArg arg, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore, TResult defaultResult = default)
            => await WaitForResultCore(await PushWithArgAsync(key, arg, layer, duplicate), defaultResult);

        private static async UniTask<TResult> WaitForResultCore<TResult>(Popup popup, TResult defaultResult)
        {
            if (popup == null)
                return defaultResult;

            if (popup is Popup<TResult> typed)
                return await typed.WaitForResultAsync(defaultResult);

            // 게임 코드 결선 오류 — 저빈도 경로라 문자열 할당 허용.
            Debug.LogError(
                $"팝업 {popup.GetType().Name}이(가) Popup<{typeof(TResult).Name}>가 아니라 결과를 받을 수 없다.",
                popup);
            return defaultResult;
        }

        /// <summary>
        /// 순차 대기열에 넣는다. 스택이 완전히 비면(로딩 중 포함 없음) 머리부터 하나씩 표시되고,
        /// 닫히면 다음이 표시된다. 보상 연출처럼 겹치지 않고 차례로 보여야 하는 팝업용.
        /// </summary>
        public void Enqueue(PopupKey key, int layer = 0)
        {
            ThrowIfDisposed();
            EnqueueCore(key, layer, null);
        }

        /// <summary>초기 데이터를 실어 순차 대기열에 넣는다. (저빈도 경로 — 데이터 보관 할당 허용)</summary>
        public void EnqueueWithArg<TArg>(PopupKey key, TArg arg, int layer = 0)
        {
            ThrowIfDisposed();
            EnqueueCore(key, layer, new QueuedPopupArg<TArg>(arg));
        }

        /// <summary>
        /// back 키(ESC/Android back)를 최상단 팝업에 라우팅한다.
        /// </summary>
        /// <returns>
        /// 키를 소비했으면 true. 스택이 비어 있을 때만 false — 게임이 종료 확인 등
        /// 다음 처리를 이어간다. 최상단이 전이 중이거나 닫기 잠금 중이면 아무것도 하지 않고
        /// 소비하며, <see cref="Popup.OnBackRequested"/>가 false를 돌려주면
        /// 닫지 않고 소비만 한다.
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

        /// <summary>최상단의 닫히는 중이 아닌 팝업을 닫는다.</summary>
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

        /// <summary>팝업을 닫는다. 닫힘 연출 완료를 기다리지 않는 fire-and-forget 버전.</summary>
        public void Close(Popup popup) => CloseAsync(popup).Forget();

        /// <summary>
        /// 팝업을 닫고 닫힘 연출·해제 완료까지 대기한다. 이 스택 소속이 아니거나 이미 닫히는
        /// 중이면 무시. 열림 연출 중이거나 닫기 잠금(<see cref="Popup.IsCloseBlocked"/>)
        /// 중이면 닫기를 예약만 하고 즉시 반환한다 — 열림 완료/마지막 잠금 해제 시 자동으로
        /// 닫힌다. 실제 닫힘까지 기다리려면 <see cref="Popup.WaitUntilClosedAsync"/>를 쓸 것.
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

            try
            {
                await popup.PlayCloseAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // Clear/Dispose가 해제를 맡는다.
            }

            if (popup.Stack != this || popup.Phase != PopupPhase.Closing)
                return;

            _stack.Remove(popup);
            popup.Detach();
            Closed?.Invoke(popup);
            NotifyStackOrderChanged();
            _releaser(popup);

            TryDrainQueue();
        }

        /// <summary>
        /// 연출을 생략하고 전부 즉시 해제한다. 진행 중인 로딩/연출은 취소되고, 순차 대기열도
        /// 비우며, 닫기 잠금도 무시한다. 씬 전환 등 강제 정리용.
        /// </summary>
        public void Clear()
        {
            var lifetime = _lifetime;
            _lifetime = new CancellationTokenSource();
            lifetime.Cancel();
            lifetime.Dispose();

            _queue.Clear();
            // _loading은 비우지 않는다 — 진행 중 로드의 finally가 자기 항목을 제거한다.
            // 토큰이 취소됐으므로 도착한 인스턴스는 스택에 들어오지 못하고 바로 해제된다.

            if (_stack.Count == 0)
                return;

            // 해제 콜백이 재-Push할 수 있어 스냅샷을 뜬다. (저빈도 경로 — 할당 허용)
            var popups = _stack.ToArray();
            _stack.Clear();

            for (int i = popups.Length - 1; i >= 0; i--)
            {
                var popup = popups[i];
                popup.Detach();
                Closed?.Invoke(popup);
                _releaser(popup);
            }
        }

        /// <summary><see cref="Clear"/> 후 스택을 더 쓸 수 없게 만든다.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Clear();
            _disposed = true;
            _lifetime.Dispose();
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
                    // Replace. ponytail: 로딩 중인 같은 키 인스턴스는 건드리지 않는다
                    // (동시 로딩 허용). 필요해지면 로딩 취소로 확장.
                    CloseAllOf(key);
                    return DuplicateDecision.Proceed;
            }
        }

        private void EnqueueCore(PopupKey key, int layer, IQueuedPopupArg arg)
        {
            _queue.Enqueue(new QueuedPopup(key, layer, arg));
            TryDrainQueue();
        }

        private Popup FocusExisting<TArg>(PopupKey key, TArg arg, bool hasArg)
        {
            var popup = FindTopmostOpen(key);
            if (popup == null)
            {
                // 로딩 중 인스턴스만 있는 경우 — 만질 대상이 없다. 인자가 실려 있었다면 유실을 표면화.
                if (hasArg)
                    Debug.LogWarning($"Focus 대상 팝업(key {key.Value})이 아직 로딩 중이라 인자가 버려졌다.");
                return null;
            }

            if (hasArg)
                DeliverArg(popup, arg);

            _stack.Remove(popup);
            InsertSorted(popup, popup.Layer); // 같은 레이어의 최상단으로
            Focused?.Invoke(popup);
            NotifyStackOrderChanged();
            return popup;
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

        /// <summary><see cref="PopupQueue"/> 전용 진입점 — 중복 정책을 거치지 않고 바로 연다.</summary>
        internal UniTask<Popup> OpenQueuedAsync(PopupKey key, int layer, IQueuedPopupArg arg)
            => arg == null
                ? OpenAsync(key, layer, (byte)0, ArgMode.None)
                : OpenAsync(key, layer, arg, ArgMode.Queued);

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
                // 팩토리가 던져도 대기열이 영구 정지하지 않게 드레인을 이어 준다.
                if (!loaded)
                    TryDrainQueue();
            }

            // 팩토리는 로드 실패를 null로 알릴 수 있다.
            if (popup == null)
            {
                TryDrainQueue();
                return null;
            }

            if (token.IsCancellationRequested)
            {
                // Clear/Dispose 이후 도착한 인스턴스 — 스택에 넣지 않고 바로 돌려보낸다.
                _releaser(popup);
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
                    // OnPopupArg가 던지면 인스턴스가 스택 밖에서 새지 않게 돌려보내고 표면화한다.
                    _releaser(popup);
                    TryDrainQueue();
                    throw;
                }
            }

            InsertSorted(popup, layer);
            popup.Attach(this, key, layer);
            Opened?.Invoke(popup);
            NotifyStackOrderChanged();

            try
            {
                await popup.PlayOpenAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Clear/Dispose가 해제를 맡는다.
            }

            if (popup.Stack != this || popup.Phase != PopupPhase.Opening)
                return null; // 열림 연출 중 Clear됨 — 이미 해제된 인스턴스를 내보내지 않는다.

            popup.SetPhase(PopupPhase.Open);

            if (popup.CloseRequested && !popup.IsCloseBlocked)
                await CloseAsync(popup);

            return popup;
        }

        internal static void DeliverArg<TArg>(Popup popup, TArg arg)
        {
            if (popup is IPopupArg<TArg> receiver)
                receiver.OnPopupArg(arg);
            else
                // 게임 코드 결선 오류 — 저빈도 경로라 문자열 할당 허용.
                Debug.LogError(
                    $"팝업 {popup.GetType().Name}이(가) IPopupArg<{typeof(TArg).Name}>를 구현하지 않아 초기 데이터를 버린다.",
                    popup);
        }

        /// <summary>
        /// 구조 변화(열림/닫힘/Focus) 후 전체 팝업에 순서를 통지하고 딤을 갱신한다.
        /// 딤 규칙: <see cref="Popup.BackgroundDim"/>을 가진 팝업 중 최상단만 켠다 —
        /// 딤 없는 팝업이 맨 위여도 그 아래 딤 보유 팝업의 딤이 유지된다.
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

        private void CloseAllOf(PopupKey key)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var popup = _stack[i];
                if (popup.Key == key && popup.Phase != PopupPhase.Closing)
                    Close(popup);
            }
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

        private void TryDrainQueue()
        {
            if (_stack.Count > 0 || _loading.Count > 0 || _queue.Count == 0)
                return;

            var next = _queue.Dequeue();
            OpenQueuedAsync(next.Key, next.Layer, next.Arg).Forget();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PopupStack));
        }

        private static void DestroyPopup(Popup popup)
        {
            if (popup)
                EditorSafeDestroy.Destroy(popup.gameObject);
        }
    }
}
