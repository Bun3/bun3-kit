using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 결과를 돌려주는 팝업의 베이스(레거시 <c>Callback(int result)</c> 대응).
    /// 확인 다이얼로그(<c>Popup&lt;bool&gt;</c>), 아이템 선택(<c>Popup&lt;ItemInstance&gt;</c>)처럼
    /// 닫힐 때 호출자에게 값 하나를 넘겨야 하는 팝업이 상속한다.
    /// </summary>
    /// <remarks>
    /// 팝업 코드는 닫기 전에 <see cref="SetResult"/>를 부르고, 호출자는
    /// <see cref="WaitForResultAsync"/>(또는 <see cref="PopupStack.PushForResultAsync{TResult}"/>)로
    /// 받는다. <see cref="SetResult"/> 없이 닫히면(back 키, 취소 버튼 등)
    /// <c>defaultResult</c>가 반환된다 — "취소"에 별도 코드가 필요 없다.
    /// 풀 재사용 시 결과는 세션마다 리셋된다.
    /// </remarks>
    public abstract class Popup<TResult> : Popup
    {
        private TResult _result;
        private bool _hasResult;

        /// <summary>닫히기 전에 결과를 기록한다. 여러 번 부르면 마지막 값이 남는다.</summary>
        protected void SetResult(TResult result)
        {
            _result = result;
            _hasResult = true;
        }

        /// <summary>
        /// 닫힐 때까지 대기한 뒤 결과를 돌려준다. <see cref="SetResult"/> 없이 닫혔으면
        /// <paramref name="defaultResult"/>. 이미 닫혀 있으면 즉시 완료.
        /// </summary>
        public async UniTask<TResult> WaitForResultAsync(TResult defaultResult = default)
        {
            await WaitUntilClosedAsync();
            return _hasResult ? _result : defaultResult;
        }

        private protected override void OnAttached()
        {
            _result = default;
            _hasResult = false;
        }
    }
}
