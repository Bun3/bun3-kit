// Connects durable commands to the shared operation transaction.
using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Rpc;

namespace Bun3.Server.Players
{
    public sealed partial class PlayerRegistry<TPlayer> where TPlayer : Player
    {
        internal async ValueTask<PlayerCommandRecord?> ExecuteCommandAsync<TResult>(PlayerOperation<TPlayer, TResult> operation,
            PlayerCommandEnvelope envelope, PlayerCommandStore commands, CancellationToken ct) where TResult : class
        {
            if (OwnedStore == null || envelope.ScopeKey != OwnedStore.Authority.ScopeKey || !envelope.Matches(operation))
                throw new PlayerCommandConflictException(envelope.Id);
            var command = new PlayerCommandExecution(envelope, commands);
            await ExecuteOperationAsync(operation, null, null, 0, ct, command).ConfigureAwait(false);
            return command.Result ?? command.Record;
        }

        private async ValueTask ResolveCommandAsync<TResult>(PlayerCommandExecution command, PlayerOperation<TPlayer, TResult> operation, PlayerOperationResult<TResult> outcome,
            PlayerOperationDiagnostics diagnostics) where TResult : class
        {
            if (outcome.Outcome != PlayerOperationOutcome.Unknown && outcome.Outcome != PlayerOperationOutcome.Failed) return;
            var record = command.Record;
            if (record == null) return;
            try
            {
                await using var scope = await _store!.OpenScopeAsync(command.Envelope.ParticipantKeys, _logger,
                    CancellationToken.None, PlayerStoreAccess.Recovery, _cleanupFailed).ConfigureAwait(false);
                record = await command.LockAsync(scope, CancellationToken.None).ConfigureAwait(false);
                if (record.IsTerminal) command.Result = record;
                else
                {
                    var receipt = await scope.ReadOperationAsync(operation.Id, CancellationToken.None).ConfigureAwait(false);
                    PlayerCommandRecord? terminal = null;
                    if (receipt != null)
                    {
                        if (!ReceiptMatches(operation, receipt)) throw new PlayerCommandConflictException(operation.Id);
                        terminal = record.With(PlayerCommandStatus.Committed, RpcStatus.Ok, receipt.ResultSchemaVersion, receipt.ResultPayload);
                    }
                    else if (outcome.Outcome == PlayerOperationOutcome.Failed) terminal = record.With(PlayerCommandStatus.Failed, outcome.Status);
                    if (terminal != null)
                    {
                        await command.CompleteAsync(scope, terminal, CancellationToken.None).ConfigureAwait(false);
                        var saved = await scope.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                        command.Result = saved.Outcome == PlayerStoreOutcome.Committed ? terminal : record.With(PlayerCommandStatus.Unknown, RpcStatus.OutcomeUnknown);
                    }
                    else command.Result = record;
                }
                if (command.Result.Status != PlayerCommandStatus.Unknown)
                {
                    foreach (var key in command.Envelope.ParticipantKeys)
                        if (_recovery.TryRemove(key, out _)) _execution.ReleaseKey(key);
                }
                diagnostics.Record("CommandResolved");
            }
            catch (Exception ex)
            {
                if (ex is PlayerStoreAcquisitionException acquisition) ObserveAuthorityFailure(acquisition.OriginalError, acquisition.Phase);
                command.Result = record.With(PlayerCommandStatus.Unknown, RpcStatus.OutcomeUnknown);
                diagnostics.Record("CommandResolutionFailed", error: ex);
            }
        }

        private static PlayerOperationResult<TResult> CommandOutcome<TResult>(PlayerCommandRecord record, IPlayerResultCodec<TResult> codec) where TResult : class
            => record.Status == PlayerCommandStatus.Committed
                ? new PlayerOperationResult<TResult>(PlayerOperationOutcome.AlreadyCommitted, RpcStatus.Ok, codec.Decode(record.ResultSchemaVersion, record.ResultPayload))
                : new PlayerOperationResult<TResult>(record.Status == PlayerCommandStatus.Rejected ? PlayerOperationOutcome.Rejected : PlayerOperationOutcome.Failed, record.StatusCode);
    }
}
