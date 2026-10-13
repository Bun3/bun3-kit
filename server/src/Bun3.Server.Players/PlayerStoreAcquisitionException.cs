using System;

namespace Bun3.Server.Players
{
    internal sealed class PlayerStoreAcquisitionException : Exception
    {
        internal PlayerStoreAcquisitionException(Exception originalError, PlayerStoreResult result, string phase)
            : base("Player store acquisition failed.", result.Error)
        { OriginalError = originalError; Result = result; Phase = phase; }

        internal Exception OriginalError { get; }
        internal PlayerStoreResult Result { get; }
        internal string Phase { get; }
    }
}
