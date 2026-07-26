namespace Bun3.Unity.UI.Buttons
{
    /// <summary>
    /// 비활성 버튼이 클릭됐을 때 사유를 재생하는 전략.
    /// </summary>
    /// <remarks>
    /// 구현체는 상태를 갖지 않아야 한다. 여러 버튼이 하나의
    /// <see cref="ButtonInteractableScope.DefaultHandler"/>를 공유하기 때문이다.
    /// </remarks>
    public interface IButtonDisabledHandler
    {
        /// <summary>사유 하나를 재생한다. 비어 있지 않은 사유만 전달된다.</summary>
        void Handle(DisabledReason reason);
    }
}
