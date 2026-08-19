// Popup partial — close locking (PopupCloseScope).
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    // Close locking: ref-count + generation token. Close requests during a lock are
    // deferred and executed on the last release.
    public abstract partial class Popup
    {
        private int _closeScopeCount;
        private int _closeScopeVersion;

        /// <summary>
        /// Whether any close lock is held. While locked, closes via <see cref="Close"/>/back
        /// are not refused but <b>deferred</b>, running automatically when the last lock releases.
        /// </summary>
        public bool IsCloseBlocked => _closeScopeCount > 0;

        /// <summary>
        /// Acquires a close lock (ref-counted, nestable). Wrap "must not close during this"
        /// sections — initial data loads, server-response waits, sequence animations — in <c>using</c>.
        /// </summary>
        /// <example><code>
        /// using (BlockClose())
        ///     await PlaySequenceAsync(ct);
        /// </code></example>
        public PopupCloseScope BlockClose()
            => new(this, AcquireCloseScope());

        /// <summary>Holds a close lock until the task finishes. The lock is released even on exception.</summary>
        public async UniTask BlockCloseWhile(UniTask task)
        {
            var version = AcquireCloseScope();
            try
            {
                await task;
            }
            finally
            {
                ReleaseCloseScope(version);
            }
        }

        /// <summary>
        /// Holds a close lock until the task finishes and returns its result.
        /// For request-response patterns (<c>var res = await BlockCloseWhile(SendPacket(...))</c>).
        /// </summary>
        public async UniTask<T> BlockCloseWhile<T>(UniTask<T> task)
        {
            var version = AcquireCloseScope();
            try
            {
                return await task;
            }
            finally
            {
                ReleaseCloseScope(version);
            }
        }

        /// <summary>
        /// Called when the close-blocked state flips (0→1 locked, 1→0 released).
        /// Hook point for game presentation such as raycast blocking or a loading spinner.
        /// Default does nothing.
        /// </summary>
        protected virtual void OnCloseBlockedChanged(bool blocked) { }

        /// <returns>Generation token to match on release — incremented per Detach to invalidate previous-session scopes.</returns>
        internal int AcquireCloseScope()
        {
            _closeScopeCount++;
            if (_closeScopeCount == 1)
                OnCloseBlockedChanged(true);

            return _closeScopeVersion;
        }

        internal void ReleaseCloseScope(int version)
        {
            // A late release from a guard acquired before Detach must not corrupt the new session's count.
            if (version != _closeScopeVersion || _closeScopeCount == 0)
                return;

            _closeScopeCount--;
            if (_closeScopeCount > 0)
                return;

            OnCloseBlockedChanged(false);

            if (CloseRequested && Phase == PopupPhase.Open)
                Stack?.Close(this);
        }
    }
}
