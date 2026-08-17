namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 같은 키의 팝업이 이미 열려 있거나 로딩 중일 때 <see cref="PopupStack.Push"/>의 처리 방식.
    /// </summary>
    public enum PopupDuplicatePolicy
    {
        /// <summary>요청을 무시한다.</summary>
        Ignore = 0,

        /// <summary>순차 대기열 끝에 넣는다. 기존 팝업들이 모두 닫히면 표시된다.</summary>
        Queue = 1,

        /// <summary>열려 있는 기존 인스턴스를 닫고 새로 연다.</summary>
        Replace = 2,

        /// <summary>
        /// 열려 있는 기존 인스턴스를 재사용한다: 같은 레이어의 최상단으로 올리고,
        /// 인자 push였다면 <see cref="IPopupArg{TArg}"/>로 인자를 재주입한다.
        /// (레거시 GetOrShowPopup 대응. 로딩 중 인스턴스만 있으면 아무것도 하지 않는다.)
        /// </summary>
        Focus = 3,
    }
}
