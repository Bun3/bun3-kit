namespace Bun3.Server.Rpc
{
    /// <summary>패킷 첫 바이트의 채널 값. 0x10 이상은 예약(게임 커스텀/고빈도 채널).</summary>
    public static class Channels
    {
        /// <summary>프레임워크 소유 제어 메시지(Ping/Pong 등).</summary>
        public const byte Control = 0x01;

        /// <summary>클라이언트 → 서버 요청.</summary>
        public const byte Request = 0x02;

        /// <summary>서버 → 클라이언트 요청 응답.</summary>
        public const byte Response = 0x03;

        /// <summary>서버 → 클라이언트 비요청 푸시.</summary>
        public const byte Update = 0x04;
    }
}
