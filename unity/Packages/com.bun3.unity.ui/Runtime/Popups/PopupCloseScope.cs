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
    public struct PopupCloseScope : IDisposable
    {
        private Popup _popup;
        private readonly int _version;

        internal PopupCloseScope(Popup popup, int version)
        {
            _popup = popup;
            _version = version;
        }

        /// <summary>잠금 하나를 해제한다. 팝업이 그 사이 닫혔다면(세대 불일치) 무시된다.</summary>
        public void Dispose()
        {
            var popup = _popup;
            _popup = null;

            if (popup != null)
                popup.ReleaseCloseScope(_version);
        }
    }
}
