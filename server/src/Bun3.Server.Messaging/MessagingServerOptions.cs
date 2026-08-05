using System;

namespace Bun3.Server.Messaging
{
    /// <summary>MessagingServer 기동 옵션.</summary>
    public sealed class MessagingServerOptions
    {
        /// <summary>이 시간 동안 아무 패킷도 안 온 세션을 킥한다. null = 비활성.</summary>
        public TimeSpan? IdleKickTimeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>세션 수신 큐 상한 (v0 Session과 동일 의미).</summary>
        public int MaxQueuedPackets { get; set; } = 256;
    }
}
