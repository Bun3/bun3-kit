// PopupStack partial — 닫기·back 처리 담당.
using System;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    // 닫기 경로: Close/Pop과 back 키 라우팅.
    public sealed partial class PopupStack
    {
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
            popup.OnTransitionStarted();

            try
            {
                await popup.PlayHideAsync(_lifetime.Token);
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
            NotifyIfEmpty();
        }

        /// <summary>
        /// 열린 팝업을 전부(또는 <paramref name="except"/>만 남기고) 정상 경로로 닫는다 —
        /// 닫힘 연출·훅·이벤트를 전부 태운다(연출 생략 강제 정리는 <see cref="Clear"/>).
        /// 레거시 HideAllPopups(without) 대응.
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

        /// <summary>조건에 맞는 팝업만 정상 경로로 닫는다. (저빈도 경로 — 델리게이트 허용)</summary>
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
