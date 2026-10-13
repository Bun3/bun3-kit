using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    /// <summary>Maps versionless player persistence guarded by durable write ownership.</summary>
    public abstract class OwnedPlayerStore<TPlayer> : PlayerPersistence<TPlayer> where TPlayer : Player
    {
        /// <summary>Creates a store bound to one ownership scope and writer.</summary>
        protected OwnedPlayerStore(PlayerWriteAuthority authority, PlayerWriteBinding binding, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
            : base(isolationLevel)
        { Authority = authority ?? throw new ArgumentNullException(nameof(authority)); binding.Validate(); Binding = binding; }
        /// <summary>Gets the coordinator protecting every persistence transaction.</summary>
        public PlayerWriteAuthority Authority { get; }
        /// <summary>Gets the immutable writer identity.</summary>
        public PlayerWriteBinding Binding { get; }
        /// <summary>Locks an existing player anchor in Authority.ScopeKey until transaction completion; returns false for missing or foreign players.</summary>
        protected abstract ValueTask<bool> LockPlayerAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct);
        /// <summary>Writes captured changes using the supplied guarded transaction without a version column.</summary>
        protected abstract ValueTask WritePlayerAsync(DbConnection db, DbTransaction tx, string key, object changes, CancellationToken ct);
        internal override async ValueTask<PlayerStoreAnchor> LockAnchorAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
            => await LockPlayerAsync(db, tx, key, ct).ConfigureAwait(false) ? PlayerStoreAnchor.Unversioned : PlayerStoreAnchor.Missing;
        internal override ValueTask WriteChangesAsync(DbConnection db, DbTransaction tx, PlayerSaveData data, CancellationToken ct)
            => WritePlayerAsync(db, tx, data.Key, data.Changes, ct);
        internal override ValueTask ProtectAsync(DbConnection db, DbTransaction tx, PlayerStoreAccess access, CancellationToken ct)
            => Authority.ProtectAsync(db, tx, Binding, access, ct);
    }
}
