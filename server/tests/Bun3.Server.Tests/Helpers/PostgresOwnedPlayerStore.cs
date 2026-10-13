using System.Data.Common;
using Bun3.Server.Players;

namespace Bun3.Server.Tests.Helpers;

internal sealed class PostgresOwnedPlayerStore(PostgresAuthorityFixture fixture, PlayerWriteBinding binding)
    : OwnedPlayerStore<ExecutionPlayer>(fixture.Authority, binding)
{
    internal Func<string, ValueTask>? Locked { get; set; }
    internal Action<int>? Attempting { get; set; }
    internal bool FailAudit { get; set; }
    internal bool LoseAcknowledgement { get; set; }
    internal Task? DelayedCommit { get; set; }
    internal PostgresFaultConnection? FaultConnection { get; private set; }
    protected override DbConnection CreateConnection()
    {
        if (LoseAcknowledgement || DelayedCommit != null)
        {
            FaultConnection = new PostgresFaultConnection(fixture.Connect(), LoseAcknowledgement, DelayedCommit);
            LoseAcknowledgement = false; DelayedCommit = null; return FaultConnection;
        }
        return fixture.Connect();
    }
    protected override async ValueTask<bool> LockPlayerAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    {
        Attempting?.Invoke(PostgresAuthorityFixture.Pid(db));
        await using var cmd = PostgresAuthorityFixture.Command(db, tx, "SELECT key FROM players WHERE scope_key=@scope AND key=@key FOR UPDATE");
        cmd.Parameters.AddWithValue("scope", Authority.ScopeKey); cmd.Parameters.AddWithValue("key", key);
        var exists = await cmd.ExecuteScalarAsync(ct) != null;
        if (Locked != null) await Locked(key); return exists;
    }
    protected override async ValueTask<ExecutionPlayer> LoadPlayerAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    {
        await using var cmd = PostgresAuthorityFixture.Command(db, tx, "SELECT gold FROM players WHERE key=@key"); cmd.Parameters.AddWithValue("key", key);
        return new ExecutionPlayer { Gold = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) };
    }
    protected override object CaptureChanges(ExecutionPlayer player) => player.Gold;
    protected override async ValueTask WritePlayerAsync(DbConnection db, DbTransaction tx, string key, object changes, CancellationToken ct)
    {
        await using var cmd = PostgresAuthorityFixture.Command(db, tx, "UPDATE players SET gold=@gold WHERE scope_key=@scope AND key=@key");
        cmd.Parameters.AddWithValue("scope", Authority.ScopeKey); cmd.Parameters.AddWithValue("key", key); cmd.Parameters.AddWithValue("gold", changes);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Locked player disappeared.");
    }
    protected override async ValueTask<PlayerOperationRecord?> ReadOperationAsync(DbConnection db, DbTransaction tx, Guid id, CancellationToken ct)
    {
        await using var cmd = PostgresAuthorityFixture.Command(db, tx, "SELECT kind,actor_kind,actor,participants,fingerprint,schema_version,result FROM operations WHERE id=@id");
        cmd.Parameters.AddWithValue("id", id); await using var row = await cmd.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        return new(id, row.GetString(0), new PlayerOperationActor((PlayerOperationActorKind)row.GetInt32(1), row.GetString(2)), (string[])row[3], (byte[])row[4], row.GetInt32(5), (byte[])row[6]);
    }
    protected override async ValueTask WriteOperationAsync(DbConnection db, DbTransaction tx, PlayerOperationRecord record, CancellationToken ct)
    {
        await using var cmd = PostgresAuthorityFixture.Command(db, tx, "INSERT INTO operations VALUES(@id,@kind,@actor_kind,@actor,@participants,@fingerprint,@schema,@result)");
        cmd.Parameters.AddWithValue("id", record.Id); cmd.Parameters.AddWithValue("kind", record.Kind); cmd.Parameters.AddWithValue("actor_kind", (int)record.Actor.Kind);
        cmd.Parameters.AddWithValue("actor", record.Actor.Key); cmd.Parameters.AddWithValue("participants", record.ParticipantKeys.ToArray());
        cmd.Parameters.AddWithValue("fingerprint", record.InputFingerprint.ToArray()); cmd.Parameters.AddWithValue("schema", record.ResultSchemaVersion);
        cmd.Parameters.AddWithValue("result", record.ResultPayload.ToArray()); await cmd.ExecuteNonQueryAsync(ct);
    }
    protected override async ValueTask WriteAuditAsync(DbConnection db, DbTransaction tx, string key, IReadOnlyList<PlayerAuditRecord> records, CancellationToken ct)
    {
        foreach (var record in records)
        {
            await using var cmd = PostgresAuthorityFixture.Command(db, tx, "INSERT INTO audits VALUES(@key,@id,@event,@payload)");
            cmd.Parameters.AddWithValue("key", key); cmd.Parameters.AddWithValue("id", record.OperationId); cmd.Parameters.AddWithValue("event", record.EventName);
            cmd.Parameters.AddWithValue("payload", record.Payload.ToArray()); await cmd.ExecuteNonQueryAsync(ct);
        }
        if (FailAudit) throw new InvalidOperationException("Injected audit failure.");
    }
}
