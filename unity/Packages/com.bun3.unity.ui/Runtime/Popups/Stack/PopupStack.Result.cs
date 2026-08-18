using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    // 결과 경로: Popup<TResult>를 열고 닫힘 결과까지 대기하는 Push 계열.
    public sealed partial class PopupStack
    {
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

        /// <summary>
        /// 결과 팝업을 타입 키로 열고 결과까지 대기한다. <c>where TPopup : Popup&lt;TResult&gt;</c>
        /// 제약이라 팝업↔결과 타입 불일치는 <b>컴파일 에러</b>다:
        /// <c>PushForResultAsync&lt;ConfirmPopup, bool&gt;()</c>.
        /// </summary>
        public async UniTask<TResult> PushForResultAsync<TPopup, TResult>(string popupName = null,
            int layer = 0, PopupDuplicatePolicy duplicate = PopupDuplicatePolicy.Ignore,
            TResult defaultResult = default) where TPopup : Popup<TResult>
            => await WaitForResultCore(
                await PushAsync(PopupKey.Of<TPopup>(popupName), layer, duplicate), defaultResult);

        /// <summary>결과 팝업을 타입 키 + 초기 데이터로 연다. 세 타입 모두 명시한다.</summary>
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

            // 게임 코드 결선 오류 — 저빈도 경로라 문자열 할당 허용.
            Debug.LogError(
                $"팝업 {popup.GetType().Name}이(가) Popup<{typeof(TResult).Name}>가 아니라 결과를 받을 수 없다.",
                popup);
            return defaultResult;
        }
    }
}
