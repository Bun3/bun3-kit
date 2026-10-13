using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    internal sealed class PlayerCommandExecution
    {
        internal PlayerCommandExecution(PlayerCommandEnvelope envelope, PlayerCommandStore store) { Envelope = envelope; Store = store; }
        internal PlayerCommandEnvelope Envelope { get; }
        internal PlayerCommandStore Store { get; }
        internal PlayerCommandRecord? Record { get; private set; }
        internal PlayerCommandRecord? Result { get; set; }
        internal async ValueTask<PlayerCommandRecord> LockAsync<TPlayer>(PlayerStoreScope<TPlayer> scope, CancellationToken ct) where TPlayer : Player
        {
            var record = await Store.Read(scope.Connection, scope.Transaction, Envelope.Id, true, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Accepted command disappeared.");
            if (!Envelope.Matches(record.Envelope)) throw new PlayerCommandConflictException(Envelope.Id);
            if (record.Status == PlayerCommandStatus.Unknown) throw new InvalidOperationException("Unknown cannot be a durable command status.");
            Record = record; return record;
        }
        internal ValueTask CompleteAsync<TPlayer>(PlayerStoreScope<TPlayer> scope, PlayerCommandRecord result, CancellationToken ct) where TPlayer : Player
            => Store.Complete(scope.Connection, scope.Transaction, result, ct);
    }
}
