namespace Bun3.Server.Rpc
{
    /// <summary>Framework-reserved status codes (1-99). Negative values are game-defined.</summary>
    public static class RpcStatus
    {
        /// <summary>Success.</summary>
        public const int Ok = 0;

        /// <summary>Handler not registered — impossible after startup validation; defensive.</summary>
        public const int UnregisteredHandler = 1;

        /// <summary>Handler threw (default OnHandlerError policy).</summary>
        public const int HandlerException = 2;

        /// <summary>Unauthenticated — rejected by the OnGateRequest gate.</summary>
        public const int Unauthenticated = 3;
    }
}
