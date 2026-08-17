using System;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// <see cref="Popup.BlockClose"/>가 돌려주는 닫기 잠금 스코프.
    /// Dispose하면 잠금 하나가 해제된다(ref-count). <c>using</c>과 함께 쓸 것.
    /// </summary>
    /// <remarks>
    /// 복사하면 사본마다 Dispose가 각각 잠금을 해제하므로 복사하지 말 것.
    /// await를 건너는 <c>using</c> 블록에서 쓸 수 있도록 ref struct가 아니다.
    /// </remarks>
    public struct PopupCloseGuard : IDisposable
    {
        private Popup _popup;

        internal PopupCloseGuard(Popup popup) => _popup = popup;

        public void Dispose()
        {
            var popup = _popup;
            _popup = null;

            if (popup != null)
                popup.ReleaseCloseGuard();
        }
    }
}
