using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Players
{
    /// <summary>Accepts durable administrator commands independently of the active executor.</summary>
    public sealed class PlayerCommandQueue
    {
        internal PlayerCommandStore Store { get; }
        private readonly ILogger _logger;
        /// <summary>Creates a queue over the command database mapping.</summary>
        public PlayerCommandQueue(PlayerCommandStore store, ILogger? logger = null)
        { Store = store ?? throw new ArgumentNullException(nameof(store)); _logger = logger ?? NullLogger.Instance; }
        /// <summary>Accepts a command once, rejecting ID reuse with different input.</summary>
        public async ValueTask<PlayerCommandRecord> SubmitAsync(PlayerCommandEnvelope command, CancellationToken ct = default)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            return await RunAsync(async (db, tx) =>
            {
                var record = await Store.Insert(db, tx, command, ct).ConfigureAwait(false);
                if (!command.Matches(record.Envelope)) throw new PlayerCommandConflictException(command.Id);
                return record;
            }, ct, true).ConfigureAwait(false);
        }
        /// <summary>Reads the latest command status, or null if the ID is absent.</summary>
        public ValueTask<PlayerCommandRecord?> GetAsync(Guid commandId, CancellationToken ct = default)
        {
            if (commandId == Guid.Empty) throw new ArgumentException("A command ID is required.", nameof(commandId));
            return RunAsync((db, tx) => Store.Read(db, tx, commandId, false, ct), ct);
        }
        /// <summary>Finds pending candidates without claiming execution ownership or FIFO ordering.</summary>
        public ValueTask<IReadOnlyList<Guid>> FindPendingAsync(string scopeKey, int limit, CancellationToken ct = default)
            => FindPendingAsync(scopeKey, limit, null, ct);
        /// <summary>Finds supported pending candidates, filtering before the batch limit so unsupported input cannot block progress.</summary>
        public ValueTask<IReadOnlyList<Guid>> FindPendingAsync(string scopeKey, int limit,
            IReadOnlyList<(string Kind, int InputSchemaVersion)>? supportedInputs, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(scopeKey)) throw new ArgumentException("A scope key is required.", nameof(scopeKey));
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
            (string Kind, int InputSchemaVersion)[]? inputs = null;
            if (supportedInputs != null)
            {
                inputs = new (string Kind, int InputSchemaVersion)[supportedInputs.Count];
                for (var i = 0; i < inputs.Length; i++)
                {
                    inputs[i] = supportedInputs[i];
                    if (string.IsNullOrWhiteSpace(inputs[i].Kind) || inputs[i].InputSchemaVersion < 1)
                        throw new ArgumentException("Supported inputs require a kind and positive schema version.", nameof(supportedInputs));
                }
                if (inputs.Length == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    return new ValueTask<IReadOnlyList<Guid>>(Array.Empty<Guid>());
                }
            }
            return RunAsync((db, tx) => Store.Find(db, tx, scopeKey, limit, inputs, ct), ct);
        }
        private async ValueTask<TResult> RunAsync<TResult>(Func<DbConnection, DbTransaction, ValueTask<TResult>> action, CancellationToken ct, bool write = false)
        {
            DbConnection? db = null; DbTransaction? tx = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                db = Store.Create(); await db.OpenAsync(ct).ConfigureAwait(false);
                tx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
                var result = await action(db, tx).ConfigureAwait(false);
                if (write)
                {
                    ct.ThrowIfCancellationRequested();
                    try { await tx.CommitAsync(ct).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        if (result is PlayerCommandRecord record)
                        {
                            try { _logger.LogWarning(new EventId(4120, "PlayerCommandAcceptanceUnknown"), ex,
                                "Command acceptance unknown {CommandId} scope {ScopeKey} actor {ActorKey}", record.Envelope.Id, record.Envelope.ScopeKey, record.Envelope.Actor.Key); } catch { }
                            return (TResult)(object)record.With(PlayerCommandStatus.Unknown, Bun3.Server.Rpc.RpcStatus.OutcomeUnknown);
                        }
                        throw;
                    }
                }
                return result;
            }
            finally
            {
                if (tx != null) { try { await tx.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { CleanupError(ex); } }
                if (db != null) { try { await db.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { CleanupError(ex); } }
            }
        }
        private void CleanupError(Exception error)
        {
            try { _logger.LogError(new EventId(4121, "PlayerCommandCleanupFailed"), error, "Command store cleanup failed"); } catch { }
        }
    }
}
