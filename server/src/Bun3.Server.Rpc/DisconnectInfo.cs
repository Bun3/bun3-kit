using System;

namespace Bun3.Server.Rpc
{
    /// <summary>Disconnect notification payload. Code 0 = no Disconnect received (network drop or voluntary Close) —
    /// received = intentional kick (show UI), not received = accident (reconnect path).</summary>
    public readonly struct DisconnectInfo
    {
        /// <summary>Disconnect reason — 1-99 framework (DisconnectCode), negative game-defined, 0 not received.</summary>
        public int Code { get; }

        /// <summary>Transport-layer error, if any.</summary>
        public Exception? Error { get; }

        /// <summary>Whether a reason was delivered.</summary>
        public bool HasReason => Code != 0;

        /// <summary>Creates the notification payload.</summary>
        public DisconnectInfo(int code, Exception? error)
        {
            Code = code;
            Error = error;
        }
    }
}
