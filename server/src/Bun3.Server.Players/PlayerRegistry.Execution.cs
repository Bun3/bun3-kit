// Coordinates session execution and invalidation under shared player ownership.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Players
{
    public sealed partial class PlayerRegistry<TPlayer> where TPlayer : Player
    {
        internal ValueTask<PlayerExecutionScope> AcquireAsync(string key) => _execution.AcquireAsync(key, default);

        internal bool SupportsAudit => _store != null;

        internal bool IsCurrent(PlayerSession<TPlayer> session, TPlayer player, long generation) =>
            System.Threading.Volatile.Read(ref _authorityLost) == 0 && session.Connection.IsOpen && session.BindingGeneration == generation
            && ReferenceEquals(session.Player, player) && player.IsValid
            && _entries.TryGetValue(player.AccountKey, out var entry) && entry.Valid
            && ReferenceEquals(entry.Player, player) && ReferenceEquals(entry.Session, session);

        internal async ValueTask InvalidateAsync(IReadOnlyList<string> keys, bool requireRecovery, Exception cause)
        {
            for (var i = 0; i < keys.Count; i++)
                await InvalidateKeyAsync(keys[i], requireRecovery, cause).ConfigureAwait(false);
        }

        private async ValueTask InvalidateKeyAsync(string key, bool requireRecovery, Exception cause)
        {
            _logger.LogError(cause, "Player {AccountKey} invalidated", key);
            _discarded.TryAdd(key, 0);
            if (requireRecovery && _recovery.TryAdd(key, 0)) _execution.RetainKey(key);
            if (!_entries.TryRemove(key, out var entry)) return;
            entry.Valid = false;
            entry.Player.IsValid = false;
            entry.Player.DiscardAudit();
            var session = entry.Session;
            entry.Session = null;
            entry.Player.CurrentSession = null;
            session?.SetPlayer(null);
            _execution.ReleaseKey(key);
            Disconnect(session, DisconnectCode.PersistenceFailure, key);
            await ReleaseAsync(entry.Player, PlayerReleaseReason.Invalidated).ConfigureAwait(false);
        }

        private void Disconnect(PlayerSession<TPlayer>? session, int code, string key)
        {
            try { session?.Kick(code); }
            catch (Exception ex) { RecordCleanupFailure(ex); _logger.LogError(ex, "Player disconnect failed ({AccountKey})", key); }
        }

        private async ValueTask ReleaseAsync(TPlayer player, PlayerReleaseReason reason)
        {
            try { await player.OnReleasedAsync(reason).ConfigureAwait(false); }
            catch (Exception ex) { RecordCleanupFailure(ex); _logger.LogError(ex, "Player release hook failed ({AccountKey})", player.AccountKey); }
        }
    }
}
