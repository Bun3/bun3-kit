namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 팝업 생명주기 단계. <see cref="PopupStack"/>이 전이시킨다.
    /// </summary>
    public enum PopupPhase
    {
        /// <summary>스택에 속해 있지 않다.</summary>
        None = 0,

        /// <summary>스택에 삽입됐고 열림 연출(<c>PlayOpenAsync</c>) 대기 중.</summary>
        Opening = 1,

        /// <summary>열림 완료. back 키 라우팅 대상이 될 수 있다.</summary>
        Open = 2,

        /// <summary>닫힘 연출(<c>PlayCloseAsync</c>) 대기 중.</summary>
        Closing = 3,
    }
}
