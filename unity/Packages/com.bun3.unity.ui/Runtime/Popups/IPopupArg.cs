namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// <see cref="PopupStack.PushAsync{TArg}"/>로 전달된 초기 데이터를 받는 팝업이 구현한다.
    /// </summary>
    /// <remarks>
    /// 팩토리 로딩이 끝난 직후, 스택 삽입과 열림 연출(<c>PlayOpenAsync</c>) 전에 호출된다 —
    /// 호출 시점에는 아직 <see cref="PopupBehaviour.Stack"/>이 null이다.
    /// 레거시처럼 인스턴스를 동기 생성해서 초기화 메서드를 부를 필요가 없도록,
    /// 비동기 로딩과 초기 데이터 전달을 스택이 이어 준다.
    /// </remarks>
    public interface IPopupArg<in TArg>
    {
        /// <summary>Push/Enqueue에 실려 온 초기 데이터를 받는다.</summary>
        void OnPopupArg(TArg arg);
    }
}
