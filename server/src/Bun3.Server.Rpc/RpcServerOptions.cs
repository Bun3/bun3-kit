using System;

namespace Bun3.Server.Rpc
{
    /// <summary>RpcServer startup options.</summary>
    public sealed class RpcServerOptions
    {
        /// <summary>
        /// Kicks a session that received no packet for this long. null = disabled.
        /// Receive time is measured at packet processing start, so a handler running longer than this can kick its own session — take care when lowering the timeout.
        /// </summary>
        public TimeSpan? IdleKickTimeout { get; set; } = TimeSpan.FromSeconds(120);

        /// <summary>Session receive queue cap (same meaning as v0 Session).</summary>
        public int MaxQueuedPackets { get; set; } = 256;

        /// <summary>Logs a warning when a session queue item (handler or Post work) exceeds this duration.
        /// Never aborts the work (serialization preserved). Zero or less = monitoring off.</summary>
        public TimeSpan SlowWorkWarning { get; set; } = TimeSpan.FromSeconds(1);
    }
}
