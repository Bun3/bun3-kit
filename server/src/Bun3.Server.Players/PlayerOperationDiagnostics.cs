using System;
using System.Data.Common;
using System.Diagnostics;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
namespace Bun3.Server.Players
{
    internal sealed class PlayerOperationDiagnostics
    {
        private readonly ILogger _logger;
        private readonly Guid _operationId;
        private readonly string _kind;
        private readonly IReadOnlyList<string>? _participantKeys;
        private readonly PlayerOperationActor? _actor;
        private readonly string? _scopeKey;
        private readonly Guid? _instanceId;
        private readonly Guid _saveAttemptId = Guid.NewGuid();
        private readonly long _started = Stopwatch.GetTimestamp();
        internal PlayerOperationDiagnostics(ILogger logger, Guid operationId, string kind, IReadOnlyList<string>? participantKeys = null,
            PlayerOperationActor? actor = null, string? scopeKey = null, Guid? instanceId = null)
        { _logger = logger; _operationId = operationId; _kind = kind; _participantKeys = participantKeys; _actor = actor; _scopeKey = scopeKey; _instanceId = instanceId; }
        internal void Record(string stage, string? key = null, string? mode = null, long? generation = null,
            long? expectedVersion = null, long? committedVersion = null, Exception? error = null, string? phase = null)
        {
            try
            {
                _logger.Log(error == null ? LogLevel.Information : LogLevel.Error, new EventId(4200, "PlayerOperationStage"), error,
                    "Player operation {OperationId} {OperationKind} save {SaveAttemptId} participants {ParticipantKeys} participant {AccountKey} mode {ParticipantMode} generation {MemoryGeneration} expected {ExpectedVersion} committed {CommittedVersion} stage {Stage} phase {Phase} elapsed {ElapsedMilliseconds} error {ErrorType} database {DbErrorCode} actor kind {ActorKind} actor {ActorKey} scope {ScopeKey} instance {InstanceId}",
                    _operationId, _kind, _saveAttemptId, _participantKeys, key, mode, generation, expectedVersion, committedVersion, stage, phase,
                    (Stopwatch.GetTimestamp() - _started) * 1000.0 / Stopwatch.Frequency, error?.GetType().FullName, FindDatabaseError(error)?.ErrorCode, _actor?.Kind, _actor?.Key, _scopeKey, _instanceId);
            }
            catch { }
        }
        private static DbException? FindDatabaseError(Exception? error)
        {
            if (error is DbException database) return database;
            if (error is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions)
                {
                    var found = FindDatabaseError(inner);
                    if (found != null) return found;
                }
            return error?.InnerException == null ? null : FindDatabaseError(error.InnerException);
        }
    }
}
