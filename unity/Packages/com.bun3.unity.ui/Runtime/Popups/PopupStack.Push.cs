// PopupStack partial — 열기(push)·중복 정책 담당.
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    // 열기 경로: Push 계열(원시 키/타입 키), 중복 정책 판정, 실제 열림 시퀀스(OpenAsync).
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

        // ── 타입 = 키 (기본 기조, 레거시 ShowPopup<T> 대응) ──
        // popupName은 같은 클래스로 다른 프리팹을 띄우는 변형용 — null이면 클래스 이름이 키.

        /// <summary>타입을 키로 연다. fire-and-forget.</summary>
        public void Push<TPopup>(string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => Push(PopupKey.Of<TPopup>(popupName), layer, duplicate);

        /// <summary>타입을 키로 열고, 열림 완료 후 <b>타입된 인스턴스</b>를 돌려준다(캐스팅 불필요).</summary>
        public async UniTask<TPopup> PushAsync<TPopup>(string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => CastOrNull<TPopup>(await PushAsync(PopupKey.Of<TPopup>(popupName), layer, duplicate));

        /// <summary>타입을 키로, 초기 데이터를 실어 연다. fire-and-forget.</summary>
        public void PushWithArg<TPopup, TArg>(TArg arg, string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => PushWithArg(PopupKey.Of<TPopup>(popupName), arg, layer, duplicate);

        /// <summary>타입을 키로, 초기 데이터를 실어 열고 타입된 인스턴스를 돌려준다.</summary>
        public async UniTask<TPopup> PushWithArgAsync<TPopup, TArg>(TArg arg, string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
            => CastOrNull<TPopup>(await PushWithArgAsync(PopupKey.Of<TPopup>(popupName), arg, layer, duplicate));

        // ── configure 채널 (레거시 Popup_Alert fluent 빌더 대응) ──
        // 게임 팝업의 fluent 세터 체인을 "비동기 로딩 완료 후, 열림 연출 전"에 실행한다 —
        // 레거시처럼 인스턴스를 동기 생성하지 않고도 Show().SetTitle().SetDesc() DX를 유지한다.
        // (저빈도 다이얼로그 경로 — 클로저/캐리어 할당 허용)

        /// <summary>구성 체인을 실어 연다. fire-and-forget.</summary>
        public void Push<TPopup>(Action<TPopup> configure, string popupName = null, int layer = 0,
            PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore) where TPopup : Popup
        {
            ThrowIfDisposed();
            PushAsync(configure, popupName, layer, duplicate).Forget();
        }

        /// <summary>
        /// 구성 체인을 실어 열고 타입된 인스턴스를 돌려준다. <paramref name="configure"/>는
        /// 로딩 완료 직후·열림 연출 전에 호출된다. <see cref="PopupDuplicatePolicy.Focus"/>면
        /// 기존 인스턴스에 재적용(레거시 GetOrShow().Set체인 대응),
        /// <see cref="PopupDuplicatePolicy.Queue"/>면 표시 시점까지 보관된다.
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
        /// 결과 팝업을 구성 체인과 함께 열고 결과까지 대기한다 — 레거시
        /// <c>Popup_Alert.Show().SetDesc(...).WaitResultAsync()</c>의 프레임워크 표준형:
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
                Debug.LogWarning($"Focus 대상 팝업({key.Name})이 아직 로딩 중이라 구성 체인이 버려졌다.");
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
                    // Replace. ponytail: 로딩 중인 같은 키 인스턴스는 건드리지 않는다
                    // (동시 로딩 허용). 필요해지면 로딩 취소로 확장.
                    CloseAllOf(key);
                    return DuplicateDecision.Proceed;
            }
        }

        private Popup FocusExisting<TArg>(PopupKey key, TArg arg, bool hasArg)
        {
            var popup = FindTopmostOpen(key);
            if (popup == null)
            {
                // 로딩 중 인스턴스만 있는 경우 — 만질 대상이 없다. 인자가 실려 있었다면 유실을 표면화.
                if (hasArg)
                    Debug.LogWarning($"Focus 대상 팝업({key.Name})이 아직 로딩 중이라 인자가 버려졌다.");
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
                {
                    TryDrainQueue();
                    NotifyIfEmpty();
                }
            }

            // 팩토리는 로드 실패를 null로 알릴 수 있다.
            if (popup == null)
            {
                TryDrainQueue();
                NotifyIfEmpty();
                return null;
            }

            if (token.IsCancellationRequested)
            {
                // Clear/Dispose 이후 도착한 인스턴스 — 스택에 넣지 않고 바로 돌려보낸다.
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
                    // OnPopupArg가 던지면 인스턴스가 스택 밖에서 새지 않게 돌려보내고 표면화한다.
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
                // Clear/Dispose가 해제를 맡는다.
            }

            if (popup.Stack != this || popup.Phase != PopupPhase.Opening)
                return null; // 열림 연출 중 Clear됨 — 이미 해제된 인스턴스를 내보내지 않는다.

            popup.SetPhase(PopupPhase.Open);
            popup.OnOpenCompleted(token);

            if (popup.CloseRequested && !popup.IsCloseBlocked)
                await CloseAsync(popup);

            return popup;
        }

        /// <summary>구성 체인을 표시 시점까지 실어 나르는 캐리어. (저빈도 — 할당 허용)</summary>
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
                        $"키 {popup.Key.Name}의 인스턴스가 {popup.GetType().Name}이라 {typeof(TPopup).Name} 구성 체인을 적용할 수 없다.",
                        popup);
            }
        }

        private static TPopup CastOrNull<TPopup>(Popup popup) where TPopup : Popup
        {
            if (popup == null)
                return null;

            if (popup is TPopup typed)
                return typed;

            // 게임 코드 결선 오류(키 이름과 프리팹 타입 불일치) — 저빈도 경로라 문자열 할당 허용.
            Debug.LogError(
                $"키 {popup.Key.Name}의 인스턴스가 {popup.GetType().Name}이라 {typeof(TPopup).Name}로 열 수 없다.",
                popup);
            return null;
        }
    }
}
