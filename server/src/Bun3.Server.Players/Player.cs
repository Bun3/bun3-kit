using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Rpc;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Players
{
    /// <summary>
    /// One per accountKey; survives reconnects. State (currency, inventory, etc.) lives in the
    /// derived class. Hooks run inside the registry's account-key stripe lock, so hooks must not
    /// call SignInAsync/Kick reentrantly (deadlock).
    /// On duplicate-login (NewWins) transfer the old session is de-authorized immediately
    /// (Player=null) so its queued requests are blocked at the gate — but one handler already
    /// executing at transfer time cannot be preempted, so PlayerTicker re-checks ownership right
    /// before running tick/save. Save points are three: periodic sweep and detach (both only when
    /// dirty), and retirement (OnRetiredAsync, regardless of dirty).
    /// </summary>
    public abstract class Player
    {
        /// <summary>Opaque identity key (recommended convention "provider:subject"). Set at SignIn.</summary>
        public string AccountKey { get; internal set; } = "";

        /// <summary>Current session while connected; null during grace.</summary>
        public RpcSession? CurrentSession { get; internal set; }

        /// <summary>Whether a session is currently attached.</summary>
        public bool IsConnected => CurrentSession != null;

        /// <summary>Called right after session binding. isReconnect=true means grace rebinding or duplicate-login transfer.</summary>
        protected internal virtual ValueTask OnAttachedAsync(bool isReconnect) => default;

        /// <summary>Called on disconnect (grace start).</summary>
        protected internal virtual ValueTask OnDetachedAsync() => default;

        /// <summary>Called on grace expiry or RetireAll — a save point. Removed from the registry afterward.</summary>
        protected internal virtual ValueTask OnRetiredAsync() => default;

        /// <summary>Pushes to the current session and returns true while connected; false during grace.</summary>
        public async ValueTask<bool> PushUpdateAsync(IMessage update)
        {
            var session = CurrentSession;
            if (session == null)
            {
                return false;
            }

            await session.SendUpdateAsync(update).ConfigureAwait(false);
            return true;
        }

        internal long LastTickAtTicksUtc;    // PlayerTicker only — reset on Attach
        internal long NextSaveAtTicksUtc;    // PlayerTicker only — re-armed on Attach

        // PlayerTicker-only tick work cache — recreated only on session rebinding to avoid a per-tick closure allocation.
        // Read and written only on the tick loop thread.
        internal Func<ValueTask>? TickWork;
        internal object? TickWorkSession;

        /// <summary>Tick hook called periodically while connected — runs inside the current session actor,
        /// so it never runs concurrently with request handlers. delta is the real elapsed time since the
        /// last tick (reset on rebinding — offline spans are the game's job in OnAttachedAsync).
        /// Same constraints as handlers: keep it short, never synchronously wait on own/other session completion.</summary>
        protected internal virtual ValueTask OnTickAsync(TimeSpan delta) => default;

        /// <summary>Save hook — the game implements the DB write. Called on periodic sweep and on
        /// disconnect (detach) — both only when dirty. The final point for grace expiry is OnRetiredAsync.</summary>
        protected internal virtual ValueTask OnSaveAsync() => default;

        private int _dirtyVersion;
        private int _savedVersion;

        /// <summary>Call after mutating state — marks the player for the next save cycle.
        /// A call during an in-flight save survives as a target of the next save (version counter).</summary>
        public void MarkDirty() => Interlocked.Increment(ref _dirtyVersion);

        /// <summary>Whether changes are pending save.</summary>
        public bool IsDirty => Volatile.Read(ref _dirtyVersion) != Volatile.Read(ref _savedVersion);

        internal async ValueTask TrySaveAsync(ILogger logger)
        {
            var capturedVersion = Volatile.Read(ref _dirtyVersion);
            try
            {
                await OnSaveAsync().ConfigureAwait(false);
                Volatile.Write(ref _savedVersion, capturedVersion);   // MarkDirty during save keeps a higher version, so dirty persists
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OnSaveAsync failed — kept dirty, retrying next cycle (Player {AccountKey})", AccountKey);
            }
        }
    }
}
