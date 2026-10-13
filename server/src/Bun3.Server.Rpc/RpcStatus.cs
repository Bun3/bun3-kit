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

        /// <summary>Invalid operation input, nested execution, or closed admission.</summary>
        public const int InvalidOperation = 4;
        /// <summary>Player persistence or result preparation failed.</summary>
        public const int PersistenceFailure = 5;
        /// <summary>The database commit outcome could not be confirmed.</summary>
        public const int OutcomeUnknown = 6;
        /// <summary>An operation ID was reused with different identity or input.</summary>
        public const int OperationConflict = 7;
        /// <summary>A declared participant does not exist.</summary>
        public const int PlayerNotFound = 8;
    }
}
