namespace Bun3.Server.Rpc
{
    /// <summary>프레임워크 예약 상태코드(1~99). 음수는 게임 정의.</summary>
    public static class RpcStatus
    {
        /// <summary>정상 처리.</summary>
        public const int Ok = 0;

        /// <summary>핸들러 미등록 — 기동 검증상 불가, 방어용.</summary>
        public const int UnregisteredHandler = 1;

        /// <summary>핸들러 예외 (OnHandlerError 기본 정책).</summary>
        public const int HandlerException = 2;

        /// <summary>미인증 — OnGateRequest 게이트 거부 (Players 모듈 등).</summary>
        public const int Unauthenticated = 3;
    }
}
