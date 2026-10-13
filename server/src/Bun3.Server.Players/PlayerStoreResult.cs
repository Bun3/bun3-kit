using System;

namespace Bun3.Server.Players
{
    internal enum PlayerStoreOutcome { Committed, RolledBack, Unknown }

    internal readonly struct PlayerStoreResult
    {
        internal PlayerStoreResult(PlayerStoreOutcome outcome, Exception? error = null) { Outcome = outcome; Error = error; }
        internal PlayerStoreOutcome Outcome { get; }
        internal Exception? Error { get; }
    }
}
