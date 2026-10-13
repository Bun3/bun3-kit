// Coordinates operation admission, exclusive participants, receipts, and atomic persistence.
using System;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Rpc;

namespace Bun3.Server.Players
{
    public sealed partial class PlayerRegistry<TPlayer> where TPlayer : Player
    {
        private readonly object _operationSync = new object();
        private readonly PlayerAdmissionGate _admission = new PlayerAdmissionGate();
        private bool _admissionSealed;

        /// <summary>Stops login and operation admission. Already applied work still completes persistence and cleanup.</summary>
        public void StopAcceptingOperations()
        { lock (_operationSync) { _admissionSealed = true; _admission.Close(); } }

        /// <summary>Executes trusted server work over all declared participants in one database transaction.</summary>
        /// <remarks>Authenticate and authorize the actor before calling. Cancellation is honored only before Apply.</remarks>
        public ValueTask<PlayerOperationResult<TResult>> ExecuteAsync<TResult>(
            PlayerOperation<TPlayer, TResult> operation, CancellationToken ct = default) where TResult : class =>
            ExecuteOperationAsync(operation, null, null, 0, ct);

        internal ValueTask<PlayerOperationResult<TResult>> ExecuteForSessionAsync<TResult>(PlayerSession<TPlayer> session,
            PlayerOperation<TPlayer, TResult> operation, CancellationToken ct) where TResult : class =>
            ExecuteOperationAsync(operation, session, session.Player, session.BindingGeneration, ct);

        internal PlayerAdmissionScope EnterWork() => _retired || Volatile.Read(ref _disposed) != 0 ? default : _admission.Enter();
        private bool TryBeginOperation() => !_retired && Volatile.Read(ref _disposed) == 0 && _admission.TryEnter();
        private bool AdmissionOpen => _admission.IsOpen && !_retired && Volatile.Read(ref _disposed) == 0;
        private void EndOperation() => _admission.Exit();
        private ValueTask StopAndDrainOperationsAsync(CancellationToken ct) => _admission.DrainAsync(ct);
        internal void OpenAuthorityAdmission()
        {
            lock (_operationSync)
            {
                if (_admissionSealed || _retired || Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _authorityLost) != 0)
                    throw new InvalidOperationException("A closed registry cannot be reactivated.");
                _admission.Open();
            }
        }

        private async ValueTask<PlayerOperationResult<TResult>> ExecuteOperationAsync<TResult>(PlayerOperation<TPlayer, TResult> operation,
            PlayerSession<TPlayer>? session, TPlayer? requester, long bindingGeneration, CancellationToken ct, PlayerCommandExecution? command = null) where TResult : class
        {
            if (operation == null) return Rejected<TResult>(RpcStatus.InvalidOperation);
            var diagnostics = new PlayerOperationDiagnostics(_logger, operation.Id, operation.Kind, operation.ParticipantKeys, operation.Actor,
                OwnedStore?.Authority.ScopeKey, OwnedStore?.Binding.InstanceId);
            if (_store == null || operation.ParticipantKeys.Count > _maxOperationParticipants
                || PlayerExecutionLane.Current?.OwnsExecution == true)
            { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.InvalidOperation); }
            if (session != null && (requester == null || operation.Actor.Kind != PlayerOperationActorKind.Player || !StringComparer.Ordinal.Equals(requester.AccountKey, operation.ActorKey)))
            { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.Unauthenticated); }
            if (!TryBeginOperation()) { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.InvalidOperation); }
            using var installed = PlayerExecutionLane.Install(session);
            var lane = PlayerExecutionLane.Current!;
            var notifications = new PlayerOperationNotifications();
            lane.Notifications = notifications;
            diagnostics.Record("Waiting");
            try
            {
                lane.TakeOwnership(await _execution.AcquireAsync(operation.ParticipantKeys, ct).ConfigureAwait(false));
                if (!AdmissionOpen) { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.InvalidOperation); }
                if (session != null && !IsCurrent(session, requester!, bindingGeneration)) { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.Unauthenticated); }
                var scope = await _store.OpenScopeAsync(operation.ParticipantKeys, _logger, ct, PlayerStoreAccess.Operation, _cleanupFailed).ConfigureAwait(false);
                PlayerOperationResult<TResult> result;
                await using (scope.ConfigureAwait(false))
                {
                    result = await ExecuteOwnedOperationAsync(operation, scope, session, requester, bindingGeneration, diagnostics, notifications, ct, command).ConfigureAwait(false);
                }
                if (command != null) await ResolveCommandAsync(command, operation, result, diagnostics).ConfigureAwait(false);
                return result;
            }
            catch (PlayerStoreAcquisitionException ex)
            {
                ObserveAuthorityFailure(ex.OriginalError, ex.Phase);
                diagnostics.Record(ex.Result.Outcome == PlayerStoreOutcome.Unknown ? "CommitUnknown" : "RolledBack",
                    error: ex.Result.Error, phase: ex.Phase);
                if (ex.OriginalError is OperationCanceledException) return Rejected<TResult>(RpcStatus.InvalidOperation);
                return new PlayerOperationResult<TResult>(PlayerOperationOutcome.Failed, RpcStatus.PersistenceFailure);
            }
            catch (OperationCanceledException ex) { diagnostics.Record("Rejected", error: ex); return Rejected<TResult>(RpcStatus.InvalidOperation); }
            catch (Exception ex)
            {
                diagnostics.Record("Rejected", error: ex, phase: "Acquisition");
                return new PlayerOperationResult<TResult>(PlayerOperationOutcome.Failed, RpcStatus.PersistenceFailure);
            }
            finally
            {
                lane.ReleaseOwnership();
                lane.Notifications = null;
                await notifications.ObserveAsync(diagnostics).ConfigureAwait(false);
                EndOperation();
            }
        }

        private async ValueTask<PlayerOperationResult<TResult>> ExecuteOwnedOperationAsync<TResult>(PlayerOperation<TPlayer, TResult> operation,
            PlayerStoreScope<TPlayer> scope, PlayerSession<TPlayer>? session, TPlayer? requester, long generation, PlayerOperationDiagnostics diagnostics, PlayerOperationNotifications notifications, CancellationToken ct, PlayerCommandExecution? command) where TResult : class
        {
            var keys = operation.ParticipantKeys;
            var players = new TPlayer[keys.Count];
            var entries = new Entry?[keys.Count];
            var versions = new long?[keys.Count];
            PlayerOperationContext<TPlayer>? context = null;
            var applied = false;
            var completed = false;
            try
            {
                if (!AdmissionOpen) { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.InvalidOperation); }
                if (command != null)
                {
                    var accepted = await command.LockAsync(scope, ct).ConfigureAwait(false);
                    if (accepted.IsTerminal) { command.Result = accepted; return CommandOutcome(accepted, operation.Codec); }
                }
                var receipt = await scope.ReadOperationAsync(operation.Id, ct).ConfigureAwait(false);
                if (!AdmissionOpen) { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.InvalidOperation); }
                ct.ThrowIfCancellationRequested();
                if (receipt != null)
                {
                    if (!ReceiptMatches(operation, receipt)) { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.OperationConflict); }
                    var previous = operation.Codec.Decode(receipt.ResultSchemaVersion, receipt.ResultPayload)
                        ?? throw new InvalidOperationException("Result codec returned null.");
                    if (command != null)
                    {
                        var terminal = command.Record!.With(PlayerCommandStatus.Committed, RpcStatus.Ok, receipt.ResultSchemaVersion, receipt.ResultPayload);
                        await command.CompleteAsync(scope, terminal, ct).ConfigureAwait(false);
                        var committed = await scope.CommitAsync(ct).ConfigureAwait(false);
                        if (committed.Outcome != PlayerStoreOutcome.Committed) return StoreFailure<TResult>(committed);
                        command.Result = terminal;
                    }
                    for (var i = 0; i < keys.Count; i++)
                        if (_recovery.TryRemove(keys[i], out _)) _execution.ReleaseKey(keys[i]);
                    diagnostics.Record("Committed");
                    return new PlayerOperationResult<TResult>(PlayerOperationOutcome.AlreadyCommitted, RpcStatus.Ok, previous);
                }
                // Snapshot every cached participant before testing versions so conflicts discard all stale participants.
                for (var i = 0; i < keys.Count; i++)
                    if (_entries.TryGetValue(keys[i], out var entry)) entries[i] = entry;
                for (var i = 0; i < keys.Count; i++)
                {
                    var version = scope.GetVersion(keys[i]);
                    if (entries[i] != null && (!scope.GetAnchor(keys[i]).Exists || entries[i]!.PersistentVersion != version))
                        throw new PlayerVersionConflictException(keys[i]);
                    if (!scope.GetAnchor(keys[i]).Exists) throw new PlayerAnchorNotFoundException(keys[i]);
                    versions[i] = version;
                }
                for (var i = 0; i < keys.Count; i++)
                {
                    players[i] = entries[i]?.Player ?? await scope.LoadAsync(keys[i], ct).ConfigureAwait(false);
                    if (players[i] == null || !players[i].IsValid) throw new InvalidOperationException("Store must load fresh valid player state.");
                    players[i].AccountKey = keys[i];
                    if (!AdmissionOpen) { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.InvalidOperation); }
                    ct.ThrowIfCancellationRequested();
                }
                for (var i = 0; i < keys.Count; i++)
                {
                    diagnostics.Record("Loaded", keys[i], entries[i] == null ? "temporary" : entries[i]!.Session == null ? "grace" : "online",
                        entries[i]?.Generation, versions[i]);
                    if (_recovery.TryRemove(keys[i], out _))
                    { _execution.ReleaseKey(keys[i]); diagnostics.Record("Reloaded", keys[i], expectedVersion: versions[i]); }
                }
                context = new PlayerOperationContext<TPlayer>(operation.Id, keys, players);
                var status = operation.Validate(context);
                if (status != 0)
                {
                    status = status < 0 ? status : RpcStatus.InvalidOperation;
                    if (command != null)
                    {
                        var rejected = command.Record!.With(PlayerCommandStatus.Rejected, status);
                        await command.CompleteAsync(scope, rejected, ct).ConfigureAwait(false);
                        var committed = await scope.CommitAsync(ct).ConfigureAwait(false);
                        if (committed.Outcome != PlayerStoreOutcome.Committed) return StoreFailure<TResult>(committed);
                        command.Result = rejected;
                    }
                    diagnostics.Record("Rejected"); return Rejected<TResult>(status);
                }
                if (!AdmissionOpen) { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.InvalidOperation); }
                if (session != null && !IsCurrent(session, requester!, generation)) { diagnostics.Record("Rejected"); return Rejected<TResult>(RpcStatus.Unauthenticated); }
                ct.ThrowIfCancellationRequested();
                applied = true;
                var lane = PlayerExecutionLane.Current!;
                lane.SetMutation(players, operation.Id, SupportsAudit);
                TResult value;
                try { value = operation.Apply(context) ?? throw new InvalidOperationException("Operation returned null."); }
                finally { lane.EndMutation(); }
                diagnostics.Record("MemoryApplied");
                if (notifications.Error != null) throw notifications.Error;
                context.Close();
                // Own the payload before Decode and persistence; codecs may return mutable backing memory.
                var payload = operation.Codec.Encode(value).ToArray();
                var schema = operation.Codec.SchemaVersion;
                var frozen = operation.Codec.Decode(schema, payload) ?? throw new InvalidOperationException("Result codec returned null.");
                var record = new PlayerOperationRecord(operation.Id, operation.Kind, operation.Actor, keys,
                    operation.InputFingerprint, schema, payload);
                var saves = new PlayerSaveData[keys.Count];
                for (var i = 0; i < keys.Count; i++)
                    saves[i] = new PlayerSaveData(keys[i], versions[i], players[i].CaptureDirtyVersion(), _store!.Capture(players[i]), players[i].CaptureAudit());
                diagnostics.Record("Saving");
                for (var i = 0; i < saves.Length; i++) await scope.WriteAsync(saves[i], CancellationToken.None).ConfigureAwait(false);
                await scope.WriteOperationAsync(record, CancellationToken.None).ConfigureAwait(false);
                var commandResult = command?.Record!.With(PlayerCommandStatus.Committed, RpcStatus.Ok, schema, payload);
                if (command != null) await command.CompleteAsync(scope, commandResult!, CancellationToken.None).ConfigureAwait(false);
                var result = await scope.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                if (result.Outcome != PlayerStoreOutcome.Committed)
                {
                    diagnostics.Record(result.Outcome == PlayerStoreOutcome.Unknown ? "CommitUnknown" : "RolledBack", error: result.Error);
                    await InvalidateOperationAsync(keys, entries, result.Outcome == PlayerStoreOutcome.Unknown,
                        result.Error ?? new InvalidOperationException("Player operation commit failed.")).ConfigureAwait(false);
                    for (var i = 0; i < keys.Count; i++) diagnostics.Record("Invalidated", keys[i]);
                    return StoreFailure<TResult>(result);
                }
                completed = true;
                if (command != null) command.Result = commandResult;
                for (var i = 0; i < keys.Count; i++)
                {
                    if (entries[i] != null) entries[i]!.PersistentVersion = versions[i] + 1;
                    players[i].CompleteSave(saves[i].CapturedDirtyVersion);
                    players[i].CompleteAudit(saves[i].AuditRecords.Count);
                    diagnostics.Record("Committed", keys[i], entries[i] == null ? "temporary" : entries[i]!.Session == null ? "grace" : "online",
                        entries[i]?.Generation, versions[i], versions[i] + 1);
                    if (_recovery.TryRemove(keys[i], out _)) _execution.ReleaseKey(keys[i]);
                }
                notifications.Start();
                return new PlayerOperationResult<TResult>(PlayerOperationOutcome.Committed, RpcStatus.Ok, frozen);
            }
            catch (Exception ex)
            {
                var result = await scope.RollbackAsync(ex).ConfigureAwait(false);
                var error = result.Error ?? ex;
                diagnostics.Record(result.Outcome == PlayerStoreOutcome.Unknown ? "CommitUnknown" : "RolledBack", error: error);
                if (applied || ex is PlayerVersionConflictException)
                {
                    await InvalidateOperationAsync(keys, entries, result.Outcome == PlayerStoreOutcome.Unknown, error).ConfigureAwait(false);
                    for (var i = 0; i < keys.Count; i++) diagnostics.Record("Invalidated", keys[i]);
                }
                if (!applied && ex is PlayerAnchorNotFoundException) return Rejected<TResult>(RpcStatus.PlayerNotFound);
                if (!applied && ex is OperationCanceledException) return Rejected<TResult>(RpcStatus.InvalidOperation);
                return StoreFailure<TResult>(result);
            }
            finally
            {
                context?.Close();
                for (var i = 0; i < players.Length; i++)
                {
                    if (players[i] == null || entries[i] != null) continue;
                    players[i].IsValid = false;
                    players[i].DiscardAudit();
                    await ReleaseAsync(players[i], completed ? PlayerReleaseReason.TemporaryCompleted : PlayerReleaseReason.TemporaryAborted).ConfigureAwait(false);
                }
            }
        }

        private static bool ReceiptMatches<TResult>(PlayerOperation<TPlayer, TResult> operation, PlayerOperationRecord receipt) where TResult : class
        {
            if (receipt.Id != operation.Id || !StringComparer.Ordinal.Equals(receipt.Kind, operation.Kind)
                || !receipt.Actor.Equals(operation.Actor)
                || !receipt.InputFingerprint.Span.SequenceEqual(operation.InputFingerprint.Span)
                || receipt.ParticipantKeys.Count != operation.ParticipantKeys.Count) return false;
            for (var i = 0; i < receipt.ParticipantKeys.Count; i++)
                if (!StringComparer.Ordinal.Equals(receipt.ParticipantKeys[i], operation.ParticipantKeys[i])) return false;
            return true;
        }

        private static PlayerOperationResult<TResult> Rejected<TResult>(int status) where TResult : class =>
            new PlayerOperationResult<TResult>(PlayerOperationOutcome.Rejected, status);

        private static PlayerOperationResult<TResult> StoreFailure<TResult>(PlayerStoreResult result) where TResult : class =>
            result.Outcome == PlayerStoreOutcome.Unknown
                ? new PlayerOperationResult<TResult>(PlayerOperationOutcome.Unknown, RpcStatus.OutcomeUnknown)
                : new PlayerOperationResult<TResult>(PlayerOperationOutcome.Failed, RpcStatus.PersistenceFailure);
    }
}
