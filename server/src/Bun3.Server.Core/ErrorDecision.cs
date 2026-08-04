namespace Bun3.Server.Core
{
    /// <summary>핸들러 예외 발생 시 세션 처리 방침.</summary>
    public enum ErrorDecision
    {
        /// <summary>세션을 종료한다(기본값). 반쯤 적용된 상태를 재접속으로 복구시킨다.</summary>
        CloseSession,

        /// <summary>예외를 무시하고 다음 프레임을 계속 처리한다.</summary>
        Continue,
    }
}
