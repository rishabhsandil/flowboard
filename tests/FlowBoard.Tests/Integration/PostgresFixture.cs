using System.Data;
using Dapper;
using Npgsql;

namespace FlowBoard.Tests.Integration;

/// <summary>
/// xUnit collection-scoped fixture that prepares a real Postgres database
/// for the integration tests:
///   • Drops + recreates a dedicated `flowboard_test` database (so a stale
///     schema from a previous run never bleeds through).
///   • Applies <c>schema.sql</c> verbatim, the same DDL Neon runs in prod.
///   • Exposes the connection string and a helper to wipe row data between
///     tests without re-applying the schema.
/// Connection defaults match a vanilla Windows PostgreSQL install. Override
/// via the <c>FLOWBOARD_TEST_DB</c> env var if you run a different setup.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TestDatabaseName = "flowboard_test";

    /// <summary>Conn string pointing at the freshly built test database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var admin = ResolveAdminConnectionString();
        await ResetDatabaseAsync(admin);

        // Now build the per-database conn string and apply the schema.
        var builder = new NpgsqlConnectionStringBuilder(admin) { Database = TestDatabaseName };
        ConnectionString = builder.ConnectionString;

        await ApplySchemaAsync(ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Truncates every domain table so tests run against a clean slate.
    /// CASCADE is used because issues, comments, mentions, etc. fan out
    /// from users + projects via foreign keys.
    /// </summary>
    public async Task ResetDataAsync()
    {
        await using var c = new NpgsqlConnection(ConnectionString);
        await c.ExecuteAsync(@"
            TRUNCATE TABLE
                activities, mentions, issue_labels, labels, comments,
                refresh_tokens, password_reset_tokens, issues, sprints,
                epics, columns, boards, project_members, projects, users
            RESTART IDENTITY CASCADE;");
    }

    public NpgsqlConnection OpenConnection() => new(ConnectionString);

    // ---- internals ------------------------------------------------------

    private static string ResolveAdminConnectionString()
    {
        // The conn string MUST come from an env var so the password never
        // lands in source control. Set FLOWBOARD_TEST_DB to a full libpq
        // connection string, e.g.
        //   Host=localhost;Port=5432;Username=postgres;Password=...;Database=postgres
        var env = Environment.GetEnvironmentVariable("FLOWBOARD_TEST_DB");
        if (string.IsNullOrWhiteSpace(env))
        {
            throw new InvalidOperationException(
                "Integration tests require FLOWBOARD_TEST_DB. Example (PowerShell):\n" +
                "  $env:FLOWBOARD_TEST_DB = 'Host=localhost;Port=5432;Username=postgres;Password=YOURPWD;Database=postgres'");
        }
        return env;
    }

    private static async Task ResetDatabaseAsync(string adminConn)
    {
        await using var c = new NpgsqlConnection(adminConn);
        await c.OpenAsync();

        // Force-drop any open sessions on the test DB before DROPping it,
        // otherwise pgAdmin or a stuck previous run will block us.
        await c.ExecuteAsync($@"
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = '{TestDatabaseName}' AND pid <> pg_backend_pid();");
        await c.ExecuteAsync($@"DROP DATABASE IF EXISTS ""{TestDatabaseName}"";");
        await c.ExecuteAsync($@"CREATE DATABASE ""{TestDatabaseName}"";");
    }

    private static async Task ApplySchemaAsync(string conn)
    {
        var schemaPath = LocateSchema();
        var ddl = await File.ReadAllTextAsync(schemaPath);

        await using var c = new NpgsqlConnection(conn);
        await c.OpenAsync();
        // schema.sql is parameter-free DDL — Npgsql sends it as a single
        // simple query, which lets the function/trigger $$ blocks parse.
        await using var cmd = new NpgsqlCommand(ddl, c);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Walks up from the test binary directory until it finds the repo
    /// root containing <c>schema.sql</c>. Lets the suite work regardless of
    /// the configuration / framework subfolder layout under <c>bin/</c>.
    /// </summary>
    private static string LocateSchema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "schema.sql");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("schema.sql not found while walking up from " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// Test collection so xUnit instantiates the heavy fixture exactly once
/// across all integration tests in the assembly.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
