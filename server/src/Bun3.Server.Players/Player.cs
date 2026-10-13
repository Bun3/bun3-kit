using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Rpc;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Players
{
    /// <summary>
    /// One per account key; survives reconnects. Requests, posted work, and lifecycle hooks
    /// share exclusive execution ownership. Nested lifecycle entry is rejected. Saving failures
    /// discard memory without calling the retirement save hook.
    /// </summary>
    public abstract partial class Player
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

        /// <summary>Releases resources without saving. Called after retirement or invalidation.</summary>
        protected internal virtual ValueTask OnReleasedAsync(PlayerReleaseReason reason) => default;

        internal bool IsValid = true;

        /// <summary>Pushes to the current session and returns true while connected; false during grace.</summary>
        /// <remarks>During operation Apply, true means a frozen packet was reserved for delivery after commit.
        /// Encoding failures are synchronous. Raw connection sends and external I/O bypass this guarantee.</remarks>
        public ValueTask<bool> PushUpdateAsync(IMessage update)
        {
            var session = CurrentSession;
            if (session == null) return new ValueTask<bool>(false);
            var send = session.SendUpdateAsync(update);
            if (send.IsCompletedSuccessfully) { send.GetAwaiter().GetResult(); return new ValueTask<bool>(true); }
            return AwaitUpdateAsync(send);
        }
        private static async ValueTask<bool> AwaitUpdateAsync(ValueTask send)
        {
            await send.ConfigureAwait(false);
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

        internal int CaptureDirtyVersion() => Volatile.Read(ref _dirtyVersion);
        internal void CompleteSave(int capturedVersion) => Volatile.Write(ref _savedVersion, capturedVersion);

        internal async ValueTask TrySaveAsync(ILogger logger)
        {
            var capturedVersion = Volatile.Read(ref _dirtyVersion);
            await OnSaveAsync().ConfigureAwait(false);
            Volatile.Write(ref _savedVersion, capturedVersion);
        }
    }
}
