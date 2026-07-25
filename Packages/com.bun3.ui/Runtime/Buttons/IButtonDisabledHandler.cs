namespace Bun3.UI.Buttons
{
    /// <summary>
    /// 비활성 버튼이 클릭됐을 때 사유를 재생하는 전략.
    /// </summary>
    /// <remarks>
    /// 구현체는 상태를 갖지 않아야 한다. 여러 버튼이 하나의
    /// <see cref="ButtonInteractableScope.DefaultHandler"/>를 공유하기 때문이다.
    /// <br/>
    /// 구현체는 <b>자신을 참조하는 버튼들보다 오래 살아야 한다.</b> 사유는 클릭 시점에
    /// 재생되므로, 스코프가 위탁한 핸들러 참조는 버튼 GameObject에 상주하는
    /// <see cref="ButtonDisabledClickReceiver"/>가 계속 들고 있는다. 수명이 짧은
    /// <see cref="UnityEngine.MonoBehaviour"/>를 핸들러로 넘기면 파괴된 뒤 클릭이 들어올 수
    /// 있다(리시버가 파괴된 <see cref="UnityEngine.Object"/> 핸들러는 걸러내지만, 그 경우
    /// 사유는 조용히 유실된다). 애플리케이션 수명 전체를 사는 객체를 쓸 것.
    /// </remarks>
    public interface IButtonDisabledHandler
    {
        /// <summary>사유 하나를 재생한다. 비어 있지 않은 사유만 전달된다.</summary>
        void Handle(DisabledReason reason);
    }
}
