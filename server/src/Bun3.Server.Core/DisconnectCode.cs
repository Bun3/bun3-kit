namespace Bun3.Server.Core
{
    /// <summary>Disconnect reason codes — 1–99 reserved for the framework, negative values game-defined
    /// (same band convention as Reply.Status). Lives in Core because kicks originate across
    /// Core (queue overflow, drain), Rpc (idle, violation), and Players (duplicate login).</summary>
    public static class DisconnectCode
    {
        /// <summary>Client-side meaning only — disconnected without receiving Disconnect
        /// (network loss or voluntary Close). Never put on the wire.</summary>
        public const int None = 0;

        /// <summary>Server shutdown drain.</summary>
        public const int ServerShutdown = 1;

        /// <summary>Duplicate login (NewWins) — signed in from another device.</summary>
        public const int DuplicateLogin = 2;

        /// <summary>Idle timeout.</summary>
        public const int IdleKick = 3;

        /// <summary>Session queue overflow kick.</summary>
        public const int QueueOverflow = 4;

        /// <summary>Protocol violation judged by the Rpc layer (unknown channel, parse failure, etc.).
        /// Transport-level disconnects (packet size exceeded) cannot carry a reason.</summary>
        public const int ProtocolViolation = 5;
    }
}
