using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 메시지박스 요청: 제목 + 본문 + 버튼 라벨들. (저빈도 다이얼로그 경로 — 문자열 허용)
    /// </summary>
    public readonly struct MessageBoxRequest
    {
        /// <summary>제목. 게임이 로컬라이즈해서 넘긴다.</summary>
        public readonly string Title;

        /// <summary>본문.</summary>
        public readonly string Message;

        /// <summary>버튼 라벨들. 클릭된 인덱스가 결과가 된다.</summary>
        public readonly string[] Buttons;

        public MessageBoxRequest(string title, string message, params string[] buttons)
        {
            Title = title;
            Message = message;
            Buttons = buttons;
        }
    }

    /// <summary>
    /// "제목+본문+버튼N → 인덱스 await" 메시지박스 베이스. 게임은 프리팹과
    /// <see cref="OnRequest"/>(UI 바인딩)만 구현하면 되고, 호출은
    /// <see cref="MessageBoxExtensions.ShowMessageBoxAsync{TMessageBox}"/> 한 줄이다.
    /// 결과: 클릭된 버튼 인덱스, 버튼 없이 닫히면(back/딤) -1.
    /// </summary>
    public abstract class MessageBoxPopup : Popup<int>, IPopupArg<MessageBoxRequest>
    {
        /// <summary>이번 세션의 요청. <see cref="OnRequest"/> 시점부터 유효.</summary>
        protected MessageBoxRequest Request { get; private set; }

        /// <summary>스택이 요청을 전달하는 지점 — 직접 부르지 말 것.</summary>
        public void OnPopupArg(MessageBoxRequest request)
        {
            Request = request;
            OnRequest(in request);
        }

        /// <summary>요청을 UI에 바인딩한다(제목/본문 텍스트, 버튼 라벨·개수 조정).</summary>
        protected abstract void OnRequest(in MessageBoxRequest request);

        /// <summary>버튼 클릭 핸들러가 호출 — 해당 인덱스를 결과로 남기고 닫는다.</summary>
        protected void Choose(int buttonIndex)
        {
            SetResult(buttonIndex);
            Close();
        }
    }

    /// <summary><see cref="MessageBoxPopup"/> 호출 편의 확장.</summary>
    public static class MessageBoxExtensions
    {
        /// <summary>
        /// 메시지박스를 열고 클릭된 버튼 인덱스를 기다린다. 버튼 없이 닫히면(back/딤) -1.
        /// </summary>
        public static UniTask<int> ShowMessageBoxAsync<TMessageBox>(this PopupStack stack,
            string title, string message, params string[] buttons) where TMessageBox : MessageBoxPopup
            => stack.PushForResultAsync<TMessageBox, MessageBoxRequest, int>(
                new MessageBoxRequest(title, message, buttons), defaultResult: -1);

        /// <summary>
        /// 2버튼 확인 다이얼로그. 첫 버튼(<paramref name="confirmLabel"/>) 클릭만 true —
        /// 취소 버튼·back·딤 닫기는 전부 false로 수렴한다.
        /// </summary>
        public static async UniTask<bool> ConfirmAsync<TMessageBox>(this PopupStack stack,
            string title, string message, string confirmLabel, string cancelLabel)
            where TMessageBox : MessageBoxPopup
            => await stack.ShowMessageBoxAsync<TMessageBox>(title, message, confirmLabel, cancelLabel) == 0;
    }
}
