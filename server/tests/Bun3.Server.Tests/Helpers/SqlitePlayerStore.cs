using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Globalization;
using Bun3.Server.Players;
using Microsoft.Data.Sqlite;

namespace Bun3.Server.Tests.Helpers;

internal sealed class SqlitePlayerStore : PlayerStore<ExecutionPlayer>, IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sqlite");
    internal bool FailWrite;
    internal int FailWriteAt;
    internal int Writes;
    internal readonly TaskCompletionSource<bool> Opening = PlayerExecutionHarness.Barrier();
    internal SqlitePlayerStore() : base(IsolationLevel.Serializable)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE players(key TEXT PRIMARY KEY, version INTEGER, gold INTEGER); INSERT INTO players VALUES('guest:a',3,100); CREATE TABLE operations(id TEXT PRIMARY KEY,kind TEXT,actor TEXT,participants BLOB,fingerprint BLOB,schema_version INTEGER,result BLOB,actor_kind INTEGER NOT NULL DEFAULT 0); CREATE TABLE audits(player_key TEXT,operation_id TEXT,event_name TEXT,occurred_at TEXT,payload BLOB);";
        cmd.ExecuteNonQuery();
    }
    internal SqliteConnection Open() { var db = new SqliteConnection($"Data Source={_path};Pooling=False"); db.Open(); return db; }
    protected override DbConnection CreateConnection() { Opening.TrySetResult(true); return new SqliteConnection($"Data Source={_path};Pooling=False"); }
    protected override async ValueTask<long?> LockVersionAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    {
        using var cmd = Command(db, tx, "SELECT version FROM players WHERE key=$key", key);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value == null ? null : Convert.ToInt64(value);
    }
    protected override async ValueTask<ExecutionPlayer> LoadPlayerAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    {
        using var cmd = Command(db, tx, "SELECT gold FROM players WHERE key=$key", key);
        return new ExecutionPlayer { Gold = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) };
    }
    protected override object CaptureChanges(ExecutionPlayer player) => player.Gold;
    protected override async ValueTask<bool> WritePlayerAsync(DbConnection db, DbTransaction tx, string key, long version, object changes, CancellationToken ct)
    {
        using var cmd = Command(db, tx, "UPDATE players SET gold=$gold, version=version+1 WHERE key=$key AND version=$version", key);
        Add(cmd, "$gold", changes); Add(cmd, "$version", version);
        var count = await cmd.ExecuteNonQueryAsync(ct);
        if (FailWrite || ++Writes == FailWriteAt) throw new InvalidOperationException("write failure");
        return count == 1;
    }
    protected override async ValueTask<PlayerOperationRecord?> ReadOperationAsync(DbConnection db, DbTransaction tx, Guid id, CancellationToken ct)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT kind,actor,participants,fingerprint,schema_version,result,actor_kind FROM operations WHERE id=$id";
        Add(cmd,"$id",id.ToString("D"));
        using var row = await cmd.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        return new PlayerOperationRecord(id,row.GetString(0),new PlayerOperationActor((PlayerOperationActorKind)row.GetInt32(6), row.GetString(1)),JsonSerializer.Deserialize<string[]>((byte[])row[2])!,
            (byte[])row[3],row.GetInt32(4),(byte[])row[5]);
    }
    protected override async ValueTask WriteOperationAsync(DbConnection db, DbTransaction tx, PlayerOperationRecord record, CancellationToken ct)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO operations(id,kind,actor,participants,fingerprint,schema_version,result,actor_kind) VALUES($id,$kind,$actor,$participants,$fingerprint,$schema,$result,$actor_kind)";
        Add(cmd,"$id",record.Id.ToString("D")); Add(cmd,"$kind",record.Kind); Add(cmd,"$actor",record.ActorKey);
        Add(cmd,"$participants",JsonSerializer.SerializeToUtf8Bytes(record.ParticipantKeys)); Add(cmd,"$fingerprint",record.InputFingerprint.ToArray());
        Add(cmd,"$schema",record.ResultSchemaVersion); Add(cmd,"$result",record.ResultPayload.ToArray());
        Add(cmd,"$actor_kind",(int)record.Actor.Kind);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    protected override async ValueTask WriteAuditAsync(DbConnection db, DbTransaction tx, string key, IReadOnlyList<PlayerAuditRecord> records, CancellationToken ct)
    {
        foreach (var record in records)
        {
            using var cmd = Command(db,tx,"INSERT INTO audits VALUES($key,$id,$event,$time,$payload)",key);
            Add(cmd,"$id",record.OperationId.ToString("D")); Add(cmd,"$event",record.EventName);
            Add(cmd,"$time",record.OccurredAtUtc.ToString("O",CultureInfo.InvariantCulture)); Add(cmd,"$payload",record.Payload.ToArray());
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
    private static DbCommand Command(DbConnection db, DbTransaction tx, string sql, string key)
    { var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; Add(cmd,"$key",key); return cmd; }
    private static void Add(DbCommand cmd, string name, object value) { var p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value; cmd.Parameters.Add(p); }
    internal (long Gold, long Version) Read()
    { using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT gold,version FROM players"; using var row = cmd.ExecuteReader(); row.Read(); return (row.GetInt64(0),row.GetInt64(1)); }
    public void Dispose() => File.Delete(_path);
}
