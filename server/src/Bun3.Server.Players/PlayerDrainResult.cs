using System;
using System.Collections.Generic;

namespace Bun3.Server.Players
{
    /// <summary>Reports whether all admitted work, cached players and uncertain writes have been resolved.</summary>
    public sealed class PlayerDrainResult
    {
        internal PlayerDrainResult(bool quiescent, IReadOnlyList<string> unresolved, IReadOnlyList<string> discarded, IReadOnlyList<Exception> errors)
        { IsQuiescent = quiescent; UnresolvedKeys = unresolved; DiscardedKeys = discarded; Errors = errors; }
        /// <summary>Gets whether no work, cache, unresolved database outcome or cleanup error remains.</summary>
        public bool IsQuiescent { get; }
        /// <summary>Gets keys whose persistence or execution has not been resolved.</summary>
        public IReadOnlyList<string> UnresolvedKeys { get; }
        /// <summary>Gets keys whose failed in-memory changes were discarded.</summary>
        public IReadOnlyList<string> DiscardedKeys { get; }
        /// <summary>Gets failures that prevent confirmation of safe shutdown.</summary>
        public IReadOnlyList<Exception> Errors { get; }
    }

    /// <summary>Separates local cleanup from confirmation of the durable Offline transition.</summary>
    public sealed class PlayerServerShutdownResult
    {
        internal PlayerServerShutdownResult(PlayerDrainResult drain, PlayerWriteTransitionResult? transition)
        { Drain = drain; Transition = transition; }
        /// <summary>Gets the local drain report.</summary>
        public PlayerDrainResult Drain { get; }
        /// <summary>Gets the attempted transition, or null when cleanup could not be confirmed.</summary>
        public PlayerWriteTransitionResult? Transition { get; }
        /// <summary>Gets whether Offline was durably confirmed after safe local cleanup.</summary>
        public bool OfflineConfirmed => Drain.IsQuiescent && Transition?.Outcome == PlayerWriteTransitionOutcome.Committed && Transition.Record.State == PlayerWriteState.Offline;
    }
}
