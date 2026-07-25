using System;

namespace Bun3.UI.Buttons
{
    /// <summary>
    /// 버튼이 비활성화된 사유. 메시지 또는 동작 중 하나만 갖는다.
    /// </summary>
    public readonly struct DisabledReason
    {
        /// <summary>표시할 메시지. <see cref="DisabledAction"/>이 있으면 null이다.</summary>
        public string DisabledMessage { get; }

        /// <summary>실행할 동작. <see cref="DisabledMessage"/>가 있으면 null이다.</summary>
        public Action DisabledAction { get; }

        /// <summary>전달할 사유가 없으면 true. 이 경우 핸들러는 호출되지 않는다.</summary>
        public bool IsEmpty => DisabledMessage == null && DisabledAction == null;

        public DisabledReason(string disabledMessage)
        {
            DisabledMessage = disabledMessage;
            DisabledAction = null;
        }

        public DisabledReason(Action disabledAction)
        {
            DisabledMessage = null;
            DisabledAction = disabledAction;
        }
    }
}
