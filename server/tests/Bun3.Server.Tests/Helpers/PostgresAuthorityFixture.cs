using System.Data.Common;
using Bun3.Server.Players;
using Npgsql;
using NpgsqlTypes;
using NUnit.Framework;

namespace Bun3.Server.Tests.Helpers;

internal sealed class PostgresAuthorityFixture : IAsyncDisposable
{
    private readonly string _rootConnection;
    private readonly string _schema = "bun3_authority_" + Guid.NewGuid().ToString("N");
    internal readonly string ConnectionString;
    internal const string Scope = "test-scope";
    private PostgresAuthorityFixture(string connection)
    {
        var builder = new NpgsqlConnectionStringBuilder(connection);
        if (builder.Database != "bun3_authority_tests" || (builder.Host != "127.0.0.1" && builder.Host != "localhost"))
            throw new InvalidOperationException("Integration tests require the dedicated loopback test database.");
        builder.Pooling = false; builder.Timeout = 5; builder.CommandTimeout = 10;
        _rootConnection = builder.ConnectionString; builder.SearchPath = _schema; ConnectionString = builder.ConnectionString;
    }
    internal static async Task<PostgresAuthorityFixture> CreateAsync()
    {
        var connection = Environment.GetEnvironmentVariable("BUN3_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Ignore("BUN3_POSTGRES_TEST_CONNECTION is not configured; run run-postgres-tests.ps1.");
        var fixture = new PostgresAuthorityFixture(connection!);
        await using var db = new NpgsqlConnection(fixture._rootConnection); await db.OpenAsync();
        await using (var create = new NpgsqlCommand("CREATE SCHEMA " + fixture._schema, db)) await create.ExecuteNonQueryAsync();
        await using var scoped = fixture.Connect(); await scoped.OpenAsync();
        await using var schema = new NpgsqlCommand("""
            CREATE TABLE write_authority(scope_key text PRIMARY KEY,state int NOT NULL,instance_id uuid,transition_id uuid NOT NULL,transition_fingerprint bytea NOT NULL);
            CREATE TABLE authority_audits(id uuid PRIMARY KEY,scope_key text NOT NULL,operator_key text NOT NULL,reason text NOT NULL,state int NOT NULL);
            CREATE TABLE players(scope_key text NOT NULL,key text PRIMARY KEY,gold bigint NOT NULL);
            CREATE TABLE operations(id uuid PRIMARY KEY,kind text NOT NULL,actor_kind int NOT NULL DEFAULT 0,actor text NOT NULL,
                participants text[] NOT NULL,fingerprint bytea NOT NULL,schema_version int NOT NULL,result bytea NOT NULL);
            CREATE TABLE commands(id uuid PRIMARY KEY,scope_key text NOT NULL,actor_kind int NOT NULL,actor text NOT NULL,kind text NOT NULL,
                input_schema int NOT NULL,participants text[] NOT NULL,fingerprint bytea NOT NULL,payload bytea NOT NULL,
                accepted_at timestamptz NOT NULL DEFAULT now(),status int NOT NULL DEFAULT 0,status_code int NOT NULL DEFAULT 0,
                result_schema int NOT NULL DEFAULT 0,result bytea NOT NULL DEFAULT ''::bytea);
            CREATE TABLE audits(player_key text NOT NULL,operation_id uuid NOT NULL,event_name text NOT NULL,payload bytea NOT NULL);
            INSERT INTO players VALUES('test-scope','a',100),('test-scope','b',20),('foreign','c',30);
            """, scoped);
        await schema.ExecuteNonQueryAsync();
        await using var control = new NpgsqlCommand("INSERT INTO write_authority VALUES(@scope,0,NULL,@id,@fingerprint)", scoped);
        control.Parameters.AddWithValue("scope", Scope); control.Parameters.AddWithValue("id", Guid.NewGuid()); control.Parameters.AddWithValue("fingerprint", new byte[32]);
        await control.ExecuteNonQueryAsync(); return fixture;
    }
    internal NpgsqlConnection Connect() => new(ConnectionString);
    internal PlayerWriteAuthority Authority => new(Scope, new PostgresAuthorityStore(this));
    internal async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] values)
    {
        await using var db = Connect(); await db.OpenAsync(); await using var cmd = new NpgsqlCommand(sql, db);
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
    internal async Task WaitBlockedAsync(int pid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var db = Connect(); await db.OpenAsync(timeout.Token);
        while (true)
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE pid=@pid AND wait_event_type='Lock'", db);
            cmd.Parameters.AddWithValue("pid", pid);
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(timeout.Token)) == 1) return;
            await Task.Delay(10, timeout.Token);
        }
    }
    internal static NpgsqlCommand Command(DbConnection db, DbTransaction tx, string sql)
        => new(sql, db is PostgresFaultConnection fault ? fault.Inner : (NpgsqlConnection)db,
            tx is PostgresFaultConnection.FaultTransaction wrapped ? wrapped.Inner : (NpgsqlTransaction)tx);
    internal static int Pid(DbConnection db) => (db is PostgresFaultConnection fault ? fault.Inner : (NpgsqlConnection)db).ProcessID;
    public async ValueTask DisposeAsync()
    {
        await using var db = new NpgsqlConnection(_rootConnection); await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("DROP SCHEMA " + _schema + " CASCADE", db); await cmd.ExecuteNonQueryAsync();
    }
}

internal sealed class PostgresAuthorityStore(PostgresAuthorityFixture fixture) : PlayerWriteAuthorityStore
{
    internal Action<int, PlayerWriteLock>? Attempting { get; set; }
    protected override DbConnection CreateConnection() => fixture.Connect();
    protected override async ValueTask<PlayerWriteAuthorityRecord?> LockAsync(DbConnection db, DbTransaction tx, string key, PlayerWriteLock mode, CancellationToken ct)
    {
        Attempting?.Invoke(PostgresAuthorityFixture.Pid(db), mode);
        await using var cmd = PostgresAuthorityFixture.Command(db, tx,
            "SELECT state,instance_id,transition_id,transition_fingerprint FROM write_authority WHERE scope_key=@scope" +
            (mode == PlayerWriteLock.Shared ? " FOR SHARE" : " FOR UPDATE"));
        cmd.Parameters.AddWithValue("scope", key);
        await using var row = await cmd.ExecuteReaderAsync(ct);
        if (!await row.ReadAsync(ct)) return null;
        return new((PlayerWriteState)row.GetInt32(0), row.IsDBNull(1) ? null : row.GetGuid(1), row.GetGuid(2), (byte[])row[3]);
    }
    protected override async ValueTask WriteAsync(DbConnection db, DbTransaction tx, string scope, PlayerWriteAuthorityRecord record, PlayerWriteTransition transition, CancellationToken ct)
    {
        await using var cmd = PostgresAuthorityFixture.Command(db, tx,
            "UPDATE write_authority SET state=@state,instance_id=@owner,transition_id=@id,transition_fingerprint=@fingerprint WHERE scope_key=@scope; " +
            "INSERT INTO authority_audits VALUES(@id,@scope,@operator,@reason,@state)");
        cmd.Parameters.AddWithValue("state", (int)record.State); cmd.Parameters.AddWithValue("owner", NpgsqlDbType.Uuid, (object?)record.InstanceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", record.TransitionId); cmd.Parameters.AddWithValue("fingerprint", record.TransitionFingerprint.ToArray());
        cmd.Parameters.AddWithValue("scope", scope); cmd.Parameters.AddWithValue("operator", transition.OperatorKey); cmd.Parameters.AddWithValue("reason", transition.Reason);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    protected override async ValueTask VerifyRecoveryAsync(DbConnection db, DbTransaction tx, string scope, CancellationToken ct)
    {
        await using var cmd = PostgresAuthorityFixture.Command(db, tx, """
            SELECT count(*) FROM commands c LEFT JOIN operations o ON o.id=c.id WHERE c.scope_key=@scope AND
            (c.status=4 OR (c.status=1 AND o.id IS NULL) OR (o.id IS NOT NULL AND
             (c.status<>1 OR c.kind<>o.kind OR c.actor_kind<>o.actor_kind OR c.actor<>o.actor OR
              c.participants<>o.participants OR c.fingerprint<>o.fingerprint OR c.result_schema<>o.schema_version OR c.result<>o.result)))
            """);
        cmd.Parameters.AddWithValue("scope", scope);
        if (Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) != 0) throw new InvalidOperationException("Command and receipt recovery consistency check failed.");
    }
}
