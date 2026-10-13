using System.Data.Common;
using Bun3.Server.Players;
using Npgsql;

namespace Bun3.Server.Tests.Helpers;

internal sealed class PostgresCommandStore(PostgresAuthorityFixture fixture) : PlayerCommandStore
{
    internal bool FailCompletion { get; set; }
    protected override DbConnection CreateConnection() => fixture.Connect();
    protected override async ValueTask<PlayerCommandRecord> InsertOrReadAsync(DbConnection db, DbTransaction tx, PlayerCommandEnvelope command, CancellationToken ct)
    {
        await using var cmd = PostgresAuthorityFixture.Command(db, tx, """
            INSERT INTO commands(id,scope_key,actor_kind,actor,kind,input_schema,participants,fingerprint,payload)
            VALUES(@id,@scope,@actor_kind,@actor,@kind,@schema,@keys,@fingerprint,@payload) ON CONFLICT(id) DO NOTHING
            """);
        cmd.Parameters.AddWithValue("id", command.Id); cmd.Parameters.AddWithValue("scope", command.ScopeKey);
        cmd.Parameters.AddWithValue("actor_kind", (int)command.Actor.Kind); cmd.Parameters.AddWithValue("actor", command.Actor.Key);
        cmd.Parameters.AddWithValue("kind", command.Kind); cmd.Parameters.AddWithValue("schema", command.InputSchemaVersion);
        cmd.Parameters.AddWithValue("keys", command.ParticipantKeys.ToArray()); cmd.Parameters.AddWithValue("fingerprint", command.InputFingerprint.ToArray());
        cmd.Parameters.AddWithValue("payload", command.InputPayload.ToArray()); await cmd.ExecuteNonQueryAsync(ct);
        return (await ReadAsync(db, tx, command.Id, false, ct))!;
    }
    protected override async ValueTask<PlayerCommandRecord?> ReadAsync(DbConnection db, DbTransaction tx, Guid id, bool forUpdate, CancellationToken ct)
    {
        await using var cmd = PostgresAuthorityFixture.Command(db, tx, """
            SELECT scope_key,actor_kind,actor,kind,input_schema,participants,fingerprint,payload,accepted_at,status,status_code,result_schema,result
            FROM commands WHERE id=@id
            """ + (forUpdate ? " FOR UPDATE" : ""));
        cmd.Parameters.AddWithValue("id", id); await using var row = await cmd.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        var envelope = new PlayerCommandEnvelope(id, row.GetString(0), new((PlayerOperationActorKind)row.GetInt32(1), row.GetString(2)),
            row.GetString(3), row.GetInt32(4), (string[])row[5], (byte[])row[6], (byte[])row[7]);
        return new(envelope, new DateTimeOffset(row.GetDateTime(8)), (PlayerCommandStatus)row.GetInt32(9), row.GetInt32(10), row.GetInt32(11), (byte[])row[12]);
    }
    protected override async ValueTask<IReadOnlyList<Guid>> FindPendingAsync(DbConnection db, DbTransaction tx, string scopeKey, int limit,
        IReadOnlyList<(string Kind, int InputSchemaVersion)>? supportedInputs, CancellationToken ct)
    {
        var supported = supportedInputs == null ? "" : " AND (kind,input_schema) IN (SELECT * FROM unnest(@kinds::text[],@schemas::int[]))";
        await using var cmd = PostgresAuthorityFixture.Command(db, tx, "SELECT id FROM commands WHERE scope_key=@scope AND status=0" + supported + " ORDER BY accepted_at,id LIMIT @limit");
        if (supportedInputs != null)
        {
            cmd.Parameters.AddWithValue("kinds", supportedInputs.Select(i => i.Kind).ToArray());
            cmd.Parameters.AddWithValue("schemas", supportedInputs.Select(i => i.InputSchemaVersion).ToArray());
        }
        cmd.Parameters.AddWithValue("scope", scopeKey); cmd.Parameters.AddWithValue("limit", limit);
        await using var row = await cmd.ExecuteReaderAsync(ct); var result = new List<Guid>();
        while (await row.ReadAsync(ct)) result.Add(row.GetGuid(0)); return result;
    }
    protected override async ValueTask CompleteAsync(DbConnection db, DbTransaction tx, PlayerCommandRecord record, CancellationToken ct)
    {
        await using var cmd = PostgresAuthorityFixture.Command(db, tx,
            "UPDATE commands SET status=@status,status_code=@code,result_schema=@schema,result=@result WHERE id=@id AND status=0");
        cmd.Parameters.AddWithValue("status", (int)record.Status); cmd.Parameters.AddWithValue("code", record.StatusCode);
        cmd.Parameters.AddWithValue("schema", record.ResultSchemaVersion); cmd.Parameters.AddWithValue("result", record.ResultPayload.ToArray()); cmd.Parameters.AddWithValue("id", record.Envelope.Id);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Only the locked Pending command may be completed.");
        if (FailCompletion && record.Status == PlayerCommandStatus.Committed) throw new InvalidOperationException("Injected completion failure.");
    }
}
