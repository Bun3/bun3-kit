namespace Bun3.Server.Core
{
    /// <summary>Session policy when a handler throws.</summary>
    public enum ErrorDecision
    {
        /// <summary>Close the session (default). Lets a reconnect recover from half-applied state.</summary>
        CloseSession,

        /// <summary>Ignore the exception and keep processing the next frame.</summary>
        Continue,
    }
}
