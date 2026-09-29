// The single shared live-PostgreSQL test helper (phase 5, D-01).
//
// It is compiled into CoffeePeek.Shops.Infrastructure.Tests and linked into
// CoffeePeek.Account.Infrastructure.Tests through
//   <Compile Include="..\CoffeePeek.Shops.Infrastructure.Tests\LivePostgres\LivePostgresServer.cs"
//            Link="LivePostgres\LivePostgresServer.cs" />
// so exactly one copy of the guards decides which databases may ever be created or dropped.
// The file holds two types on purpose (LivePostgresServer and LiveDatabase<TContext>): D-01
// requires one shared helper file, and the fixture base must travel with the guards it uses.
//
// Safety model:
// - Opt-in (D-02): nothing touches a server unless COFFEEPEEK_LIVE_PG is set. Unset means every
//   LivePostgres test reports Skipped and fixtures create nothing. When set, the value is an
//   Npgsql connection string to the maintenance database `postgres` with no Password key; the
//   password comes from pgpass (%APPDATA%\postgresql\pgpass.conf) or PGPASSFILE.
// - Guards (D-03): a superuser role is refused unless COFFEEPEEK_LIVE_PG_ALLOW_SUPERUSER is
//   exactly "1"; only names matching the owned-name regex are created or dropped; throwaway
//   databases are dropped on success and on failure; the start-of-run sweep removes only this
//   role's own cp_live_* databases older than 6 hours.
// - Fail, never skip (D-04): set but unusable configuration throws, so tests FAIL.
// - No secret leaves the process: exception messages are fixed text and never contain the
//   connection string, host or password. Output is limited to the server version and DB names.

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using CoffeePeek.Shared.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;

namespace CoffeePeek.Testing;

public static class LivePostgresServer
{
    public const string Category = "LivePostgres";
    public const string EnvVar = "COFFEEPEEK_LIVE_PG";
    public const string AllowSuperuserEnvVar = "COFFEEPEEK_LIVE_PG_ALLOW_SUPERUSER";

    public const string SkipReason =
        "Live PostgreSQL gate: set COFFEEPEEK_LIVE_PG (docs/api/public-addresses.md#live-postgresql-gate).";

    public const long ShopsLockKey = 7402100101;   // PublicAddressTransaction.cs:21
    public const long AccountLockKey = 7402100401; // ProfileAddressTransaction.cs:21

    private const string MaintenanceDatabase = "postgres";
    private const string LiveApplicationName = "coffeepeek-live-tests";
    private const string LockTimeoutOptions = "-c lock_timeout=30s";
    private const string StampFormat = "yyyyMMdd't'HHmmss";
    private const int StampLength = 15;             // yyyyMMddtHHmmss
    private const int SuffixLength = 8;             // lowercase hex
    private const int MaxIdentifierLength = 63;     // NAMEDATALEN - 1; longer names are truncated

    private const int MinimumServerVersionNum = 170000;
    private const int TombstoneState = 3;

    private static readonly TimeSpan SweepAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan ProbeDeadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProbePollInterval = TimeSpan.FromMilliseconds(50);

    // \A ... \z, never ^ ... $: .NET `$` also matches before a trailing newline.
    private static readonly Regex OwnedName =
        new(@"\Acp_live_[a-z]+_[0-9]{8}t[0-9]{6}_[0-9a-f]{8}\z", RegexOptions.CultureInvariant);

    private static readonly Regex ServiceName = new(@"\A[a-z]+\z", RegexOptions.CultureInvariant);

    private static readonly Regex RegistryTable =
        new(@"\A[A-Z][A-Za-z]*PublicAddresses\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// True when <c>COFFEEPEEK_LIVE_PG</c> is non-blank. Evaluated once per process so a fixture and
    /// its test class always agree on skip versus run.
    /// </summary>
    public static bool IsConfigured { get; } =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar));

    // ---- Pure guards (no I/O) -------------------------------------------------------------

    /// <summary>
    /// Parses the admin connection string. Only the maintenance database <c>postgres</c> is accepted
    /// and a Password key is refused. Every failure uses a fixed message with no inner exception, so
    /// the input text (host, password) can never leak into test output.
    /// </summary>
    public static NpgsqlConnectionStringBuilder ParseAdminConnectionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"{EnvVar} is empty. Set it to an Npgsql connection string to the database postgres.");

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(value);
        }
        catch (Exception)
        {
            // No inner exception on purpose: the parser's message can echo keywords or values.
            throw new InvalidOperationException(
                $"{EnvVar} is not a valid Npgsql connection string. The value is not shown.");
        }

        if (!string.IsNullOrEmpty(builder.Password))
            throw new InvalidOperationException(
                $"{EnvVar} must not contain a Password. Put the password in pgpass " +
                @"(%APPDATA%\postgresql\pgpass.conf on Windows, ~/.pgpass elsewhere) " +
                "or point PGPASSFILE at a file outside the repository.");

        if (!string.Equals(builder.Database, MaintenanceDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{EnvVar} must name the maintenance database exactly: Database=postgres. " +
                "Template and application databases are refused.");

        return builder;
    }

    /// <summary>True only for exactly "1" (D-03).</summary>
    public static bool SuperuserAllowed(string? value) => string.Equals(value, "1", StringComparison.Ordinal);

    /// <summary>
    /// <c>cp_live_{service}_{yyyyMMdd't'HHmmss}_{8 hex}</c>. The service must be lowercase ASCII
    /// letters and the result must fit PostgreSQL's 63-byte identifier limit.
    /// </summary>
    public static string NewDatabaseName(string service, DateTime utcNow)
    {
        if (service is null || !ServiceName.IsMatch(service))
            throw new ArgumentException("The service must consist of lowercase ASCII letters only.", nameof(service));

        var stamp = AsUtc(utcNow).ToString(StampFormat, CultureInfo.InvariantCulture);
        var name = $"cp_live_{service}_{stamp}_{Guid.NewGuid().ToString("N")[..SuffixLength]}";

        if (!IsOwnedDatabaseName(name))
            throw new ArgumentException(
                $"The service name is too long: database names are limited to {MaxIdentifierLength} bytes.",
                nameof(service));

        return name;
    }

    /// <summary>True only for names this harness generates (and never longer than 63 bytes).</summary>
    public static bool IsOwnedDatabaseName(string? name) =>
        name is not null && name.Length <= MaxIdentifierLength && OwnedName.IsMatch(name);

    /// <summary>Double-quotes an owned name for DDL; refuses every other name.</summary>
    public static string QuoteOwnedDatabase(string name)
    {
        if (!IsOwnedDatabaseName(name))
            throw new InvalidOperationException(
                "Refusing to touch a database that is not a cp_live_* throwaway database created by this harness.");

        return "\"" + name + "\"";
    }

    /// <summary>
    /// Connection string for a throwaway database: same host, port, user and SSL settings as the
    /// admin string, never a password, a 30 s lock timeout and a recognizable application name.
    /// </summary>
    public static string ThrowawayConnectionString(NpgsqlConnectionStringBuilder admin, string databaseName)
    {
        ArgumentNullException.ThrowIfNull(admin);
        QuoteOwnedDatabase(databaseName);

        var builder = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Database = databaseName,
            Options = LockTimeoutOptions,
            ApplicationName = LiveApplicationName,
            Password = null,
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// True for an owned name whose embedded UTC stamp is valid and older than 6 hours, so a
    /// concurrent run's fresh database is never swept.
    /// </summary>
    public static bool IsSweepCandidate(string name, DateTime utcNow)
    {
        if (!IsOwnedDatabaseName(name))
            return false;

        // "..._{stamp}_{suffix}": the service is letters only, so the stamp sits at a fixed offset from the end.
        var stamp = name.Substring(name.Length - SuffixLength - 1 - StampLength, StampLength);
        if (!DateTime.TryParseExact(stamp, StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdUtc))
            return false;

        return AsUtc(utcNow) - createdUtc > SweepAge;
    }

    /// <summary>Allowlist for the registry tables that raw SQL may name (identifiers cannot be parameterized).</summary>
    public static string ValidateRegistryTable(string table)
    {
        if (table is null || !RegistryTable.IsMatch(table))
            throw new ArgumentException("Not a public-address registry table.", nameof(table));

        return table;
    }

    /// <summary>The first <see cref="PostgresException"/> in the inner-exception chain, or null.</summary>
    public static PostgresException? FindPostgres(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg)
                return pg;
        }

        return null;
    }

    // ---- Server operations ----------------------------------------------------------------

    /// <summary>
    /// Creates a UTF8 throwaway database <c>cp_live_{service}_...</c> owned by the current role and
    /// returns its connection string. Before creating anything it checks the server (PostgreSQL 17+,
    /// superuser policy, CREATEDB, pg_trgm available) and sweeps this role's own stale throwaway
    /// databases. Every refusal throws, so a set but unusable configuration FAILS tests (D-04).
    /// </summary>
    public static async Task<string> CreateDatabaseAsync(string service, CancellationToken ct)
    {
        var admin = ParseAdminConnectionString(Environment.GetEnvironmentVariable(EnvVar));
        var name = NewDatabaseName(service, DateTime.UtcNow); // validates the service before any I/O
        var quoted = QuoteOwnedDatabase(name);

        await using (var connection = await OpenMaintenanceAsync(admin, ct))
        {
            await PreflightAsync(connection, ct);
            await SweepAsync(connection, ct);

            // Plain CREATE DATABASE, as production init-databases.sql does. It cannot run in a transaction block.
            await ExecuteAsync(connection, $"CREATE DATABASE {quoted}", ct);

            try
            {
                string? encoding;
                await using (var check = new NpgsqlCommand(
                                 "SELECT pg_encoding_to_char(encoding) FROM pg_database WHERE datname = @name", connection))
                {
                    check.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Name) { Value = name });
                    encoding = await check.ExecuteScalarAsync(ct) as string;
                }

                if (!string.Equals(encoding, "UTF8", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"The throwaway database was created with encoding '{encoding}', not UTF8 " +
                        "(Cyrillic names need UTF8). Ask the server admin for a UTF8 template1.");
            }
            catch (Exception original)
            {
                await DropAfterFailureAsync(ThrowawayConnectionString(admin, name), original);
                throw;
            }
        }

        return ThrowawayConnectionString(admin, name);
    }

    /// <summary>
    /// Drops a throwaway database. The owned-name guard runs before any I/O: a non-owned name is
    /// refused without a connection attempt. Then pools are cleared (best effort) and
    /// <c>DROP DATABASE IF EXISTS ... WITH (FORCE)</c> runs from a fresh connection to <c>postgres</c>;
    /// FORCE terminates leftover sessions and is owner-only.
    /// </summary>
    public static async Task DropDatabaseAsync(string throwawayConnectionString)
    {
        NpgsqlConnectionStringBuilder target;
        try
        {
            target = new NpgsqlConnectionStringBuilder(throwawayConnectionString);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Refusing to drop: the throwaway connection string could not be parsed.");
        }

        var quoted = QuoteOwnedDatabase(target.Database ?? "");

        // Best effort only: EF's own data source keeps a separate pool. WITH (FORCE) is the real
        // mechanism, terminating any session still connected to the throwaway database (Pitfall 5).
        NpgsqlConnection.ClearAllPools();

        await using var admin = await OpenMaintenanceAsync(target, CancellationToken.None);
        await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS {quoted} WITH (FORCE)", CancellationToken.None);
    }

    /// <summary><c>SHOW server_version</c>, the only server detail tests may write to output.</summary>
    public static async Task<string> ServerVersionAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = await OpenAsync(connectionString, ct);
        await using var command = new NpgsqlCommand("SHOW server_version", connection);
        return (string)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Waits until exactly <paramref name="expected"/> sessions in this database wait (NOT granted)
    /// on the bigint advisory lock <paramref name="key"/>. Deterministic barrier for "the other
    /// session is blocked": pg_locks shows a bigint key as <c>(classid &lt;&lt; 32) | objid</c>, objsubid 1.
    /// Polls every 50 ms and throws <see cref="TimeoutException"/> after 15 s.
    /// </summary>
    public static Task WaitForAdvisoryWaitersAsync(string connectionString, long key, int expected, CancellationToken ct) =>
        PollCountAsync(connectionString,
            """
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'advisory' AND NOT granted AND objsubid = 1
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
              AND ((classid::bigint << 32) | objid::bigint) = @key
            """,
            parameters => parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Bigint) { Value = key }),
            expected, $"advisory-lock waiter(s) on key {key}", ct);

    /// <summary>
    /// Waits until exactly <paramref name="expected"/> sessions in this database wait on a heavyweight
    /// lock of the given wait event (<c>advisory</c> or <c>transactionid</c>). Same-role sessions are
    /// visible to a non-superuser, which covers every test session. Polls every 50 ms, 15 s deadline.
    /// </summary>
    public static Task WaitForLockWaitsAsync(string connectionString, string waitEvent, int expected, CancellationToken ct) =>
        PollCountAsync(connectionString,
            """
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event_type = 'Lock' AND wait_event = @event
            """,
            parameters => parameters.Add(new NpgsqlParameter("event", NpgsqlDbType.Text) { Value = waitEvent }),
            expected, $"session(s) waiting on Lock/{waitEvent}", ct);

    /// <summary>
    /// Raw, parameterized insert of one registry row (bypasses EF and the SaveChanges hooks), for
    /// constraint tests and in-flight blockers. The table must pass <see cref="ValidateRegistryTable"/>,
    /// both timestamps must be <see cref="DateTimeKind.Utc"/> and the connection must target a
    /// throwaway database.
    /// </summary>
    public static async Task InsertRegistryRowAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string table,
        Guid? ownerId, string slug, int state, long revision, DateTime createdUtc, DateTime updatedUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ValidateRegistryTable(table);
        ArgumentNullException.ThrowIfNull(slug);
        if (createdUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("timestamptz values must be DateTimeKind.Utc.", nameof(createdUtc));
        if (updatedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("timestamptz values must be DateTimeKind.Utc.", nameof(updatedUtc));
        QuoteOwnedDatabase(connection.Database);

        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO "{table}" ("Id", "OwnerId", "Slug", "State", "Revision", "CreatedAtUtc", "UpdatedAtUtc")
             VALUES (@id, @owner, @slug, @state, @revision, @created, @updated)
             """, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() });
        command.Parameters.Add(new NpgsqlParameter("owner", NpgsqlDbType.Uuid) { Value = (object?)ownerId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("slug", NpgsqlDbType.Varchar) { Value = slug });
        command.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Integer) { Value = state });
        command.Parameters.Add(new NpgsqlParameter("revision", NpgsqlDbType.Bigint) { Value = revision });
        command.Parameters.Add(new NpgsqlParameter("created", NpgsqlDbType.TimestampTz) { Value = createdUtc });
        command.Parameters.Add(new NpgsqlParameter("updated", NpgsqlDbType.TimestampTz) { Value = updatedUtc });
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Opens a session that inserts a tombstone for <paramref name="slug"/> and keeps it UNCOMMITTED:
    /// any other insert of the same slug waits on this transaction (wait event <c>transactionid</c>)
    /// until <see cref="UncommittedInsert.CommitAsync"/>, then fails with 23505. Disposing without a
    /// commit rolls back.
    /// </summary>
    public static async Task<UncommittedInsert> BeginUncommittedTombstoneAsync(string connectionString, string table,
        string slug, CancellationToken ct)
    {
        ValidateRegistryTable(table);
        RequireOwnedConnectionString(connectionString);

        var connection = await OpenAsync(connectionString, ct);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            await InsertRegistryRowAsync(connection, transaction, table, null, slug, TombstoneState, 1, now, now, ct);
            return new UncommittedInsert(connection, transaction);
        }
        catch
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Opens a connection. A failure is rethrown with a fixed message that names only exception
    /// types, SqlState and socket error codes: Npgsql's own connect errors can contain the host.
    /// </summary>
    public static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException(
                $"Could not connect to the live PostgreSQL server ({Describe(ex)}). Connection details are not shown.");
        }
    }

    /// <summary>An uncommitted insert on its own connection; see <see cref="BeginUncommittedTombstoneAsync"/>.</summary>
    public sealed class UncommittedInsert : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;
        private bool _committed;
        private bool _disposed;

        internal UncommittedInsert(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public async Task CommitAsync(CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _transaction.CommitAsync(ct);
            _committed = true;
        }

        /// <summary>Rolls back if not committed, then closes the connection.</summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                if (!_committed)
                    await _transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                // The session is already broken (e.g. terminated by DROP ... WITH (FORCE)); closing
                // the connection below ends the transaction on the server either way.
            }
            finally
            {
                await _transaction.DisposeAsync();
                await _connection.DisposeAsync();
            }
        }
    }

    // ---- Internals ------------------------------------------------------------------------

    private static async Task PreflightAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using (var version = new NpgsqlCommand("SHOW server_version_num", connection))
        {
            var versionNum = int.Parse((string)(await version.ExecuteScalarAsync(ct))!, CultureInfo.InvariantCulture);
            if (versionNum < MinimumServerVersionNum)
                throw new InvalidOperationException(
                    $"The live PostgreSQL server must be version 17 or later (server_version_num {versionNum}).");
        }

        bool isSuperuser, canCreateDb;
        await using (var role = new NpgsqlCommand(
                         "SELECT rolsuper, rolcreatedb FROM pg_roles WHERE rolname = current_user", connection))
        await using (var reader = await role.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("The current role was not found in pg_roles.");
            isSuperuser = reader.GetBoolean(0);
            canCreateDb = reader.GetBoolean(1);
        }

        if (isSuperuser && !SuperuserAllowed(Environment.GetEnvironmentVariable(AllowSuperuserEnvVar)))
            throw new InvalidOperationException(
                "Refusing to run as a superuser role. Use a dedicated LOGIN CREATEDB NOSUPERUSER role, or set " +
                $"{AllowSuperuserEnvVar}=1 for a disposable non-production server.");

        if (!isSuperuser && !canCreateDb)
            throw new InvalidOperationException("The live-test role needs CREATEDB to create throwaway databases.");

        await using (var trgm = new NpgsqlCommand(
                         "SELECT count(*) FROM pg_available_extensions WHERE name = 'pg_trgm'", connection))
        {
            if ((long)(await trgm.ExecuteScalarAsync(ct))! == 0)
                throw new InvalidOperationException(
                    "The live PostgreSQL server does not ship the pg_trgm extension (contrib); Shops migrations need it.");
        }
    }

    /// <summary>
    /// Drops this role's own owned-name throwaway databases older than 6 hours (left by a crashed or
    /// cancelled run). A database another concurrent run just created is never touched. Best effort
    /// per database: a leftover that cannot be dropped now is retried by the next run.
    /// </summary>
    private static async Task SweepAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var candidates = new List<string>();
        await using (var list = new NpgsqlCommand(
                         """
                         SELECT d.datname FROM pg_database d
                         JOIN pg_roles r ON r.oid = d.datdba
                         WHERE r.rolname = current_user AND d.datname LIKE 'cp\_live\_%'
                         """, connection))
        await using (var reader = await list.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                if (IsSweepCandidate(name, DateTime.UtcNow))
                    candidates.Add(name);
            }
        }

        foreach (var name in candidates)
        {
            try
            {
                await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {QuoteOwnedDatabase(name)} WITH (FORCE)", ct);
            }
            catch (PostgresException)
            {
                // Left for the next run's sweep; never blocks the current run.
            }
        }
    }

    private static async Task PollCountAsync(string connectionString, string sql, Action<NpgsqlParameterCollection> bind,
        int expected, string what, CancellationToken ct)
    {
        RequireOwnedConnectionString(connectionString);

        await using var connection = await OpenAsync(connectionString, ct);
        await using var command = new NpgsqlCommand(sql, connection);
        bind(command.Parameters);

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var observed = (long)(await command.ExecuteScalarAsync(ct))!;
            if (observed == expected)
                return;

            if (elapsed.Elapsed >= ProbeDeadline)
                throw new TimeoutException(
                    $"Expected {expected} {what}, observed {observed} after {ProbeDeadline.TotalSeconds:0} s.");

            // The only Task.Delay in the harness: the poll interval of a server-side probe, never a barrier.
            await Task.Delay(ProbePollInterval, ct);
        }
    }

    private static void RequireOwnedConnectionString(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Refusing: the throwaway connection string could not be parsed.");
        }

        QuoteOwnedDatabase(builder.Database ?? "");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;

    /// <summary>Unpooled connection to <c>postgres</c> derived from the given builder (never left idle on the server).</summary>
    private static Task<NpgsqlConnection> OpenMaintenanceAsync(NpgsqlConnectionStringBuilder source, CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(source.ConnectionString)
        {
            Database = MaintenanceDatabase,
            ApplicationName = LiveApplicationName,
            Pooling = false,
        };

        return OpenAsync(builder.ConnectionString, ct);
    }

    /// <summary>
    /// Cleanup after a failure: drops the throwaway database. If the drop itself fails, both
    /// exceptions surface together so the original cause is never hidden. The caller rethrows.
    /// </summary>
    internal static async Task DropAfterFailureAsync(string throwawayConnectionString, Exception original)
    {
        try
        {
            await DropDatabaseAsync(throwawayConnectionString);
        }
        catch (Exception dropFailure)
        {
            throw new AggregateException(
                "The live test setup failed and the throwaway database could not be dropped; the next run's sweep " +
                "removes it after 6 hours.", original, dropFailure);
        }
    }

    /// <summary>Secret-free description of an exception chain, e.g. "NpgsqlException &lt;- SocketException(HostNotFound)".</summary>
    private static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            parts.Add(current switch
            {
                PostgresException pg => $"PostgresException(SqlState {pg.SqlState})",
                System.Net.Sockets.SocketException socket => $"SocketException({socket.SocketErrorCode})",
                _ => current.GetType().Name,
            });
        }

        return string.Join(" <- ", parts);
    }
}

/// <summary>
/// Class-fixture base: one migrated throwaway database per test class, built with the production
/// DbContext options (<c>UseNpgsql</c> + <c>EnableDynamicJson</c>, <c>DatabaseModule.cs</c>) and the
/// production <see cref="AuditInterceptor"/> (UTC audit stamps; Npgsql rejects Kind=Unspecified).
/// Unconfigured (<c>COFFEEPEEK_LIVE_PG</c> unset) it creates nothing and never throws; the test class
/// constructor then skips via <c>Assert.SkipUnless(LivePostgresServer.IsConfigured, ...)</c> (D-02).
/// Configured but unusable, <see cref="InitializeAsync"/> throws, so the tests FAIL (D-04).
/// </summary>
public abstract class LiveDatabase<TContext> : IAsyncLifetime where TContext : DbContext
{
    private DbContextOptions<TContext>? _options;

    /// <summary>Throwaway connection string. Never write it to test output.</summary>
    public string ConnectionString { get; private set; } = "";

    public string DatabaseName { get; private set; } = "";

    /// <summary>Lowercase letters only (<c>^[a-z]+$</c>), e.g. "shops", "accountbackfill".</summary>
    protected abstract string Service { get; }

    protected abstract TContext Create(DbContextOptions<TContext> options, bool enabled);

    /// <summary>Rehearsal fixtures only: migrate to this target first, seed legacy rows, then migrate to latest.</summary>
    protected virtual string? PreFeatureMigration => null;

    /// <summary>Flag off, pre-feature schema. Runs only when <see cref="PreFeatureMigration"/> is set.</summary>
    protected virtual Task SeedLegacyAsync(TContext db, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Flag off, latest schema.</summary>
    protected virtual Task AfterMigrateAsync(TContext db, CancellationToken ct) => Task.CompletedTask;

    /// <summary>A new context on the throwaway database. Callers dispose it.</summary>
    public TContext Context(bool enabled = true)
    {
        if (_options is null)
            throw new InvalidOperationException(
                "The live database is not initialized: COFFEEPEEK_LIVE_PG is unset or InitializeAsync has not run.");

        return Create(_options, enabled);
    }

    public async ValueTask InitializeAsync()
    {
        if (!LivePostgresServer.IsConfigured)
            return;

        var ct = TestContext.Current.CancellationToken;
        var connectionString = await LivePostgresServer.CreateDatabaseAsync(Service, ct);
        ConnectionString = connectionString;

        try
        {
            DatabaseName = new NpgsqlConnectionStringBuilder(connectionString).Database!;

            // Built once per fixture: one EF data source, so DROP ... WITH (FORCE) has one pool to end.
            _options = new DbContextOptionsBuilder<TContext>()
                .UseNpgsql(connectionString, npgsql => npgsql.ConfigureDataSource(ds => ds.EnableDynamicJson()))
                .AddInterceptors(new AuditInterceptor())
                .Options;

            if (PreFeatureMigration is { } target)
            {
                await using (var db = Create(_options, enabled: false))
                    await db.GetService<IMigrator>().MigrateAsync(target, ct);

                await using (var db = Create(_options, enabled: false))
                    await SeedLegacyAsync(db, ct);
            }

            await using (var db = Create(_options, enabled: false))
                await db.Database.MigrateAsync(ct);

            await using (var db = Create(_options, enabled: false))
                await AfterMigrateAsync(db, ct);
        }
        catch (Exception original)
        {
            _options = null;
            ConnectionString = "";
            DatabaseName = "";

            // xUnit v3 does not call DisposeAsync when InitializeAsync throws (research Pitfall 6):
            // drop here, then let the failure surface (D-03, D-04).
            await LivePostgresServer.DropAfterFailureAsync(connectionString, original);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (ConnectionString.Length == 0)
            return;

        var connectionString = ConnectionString;
        _options = null;
        ConnectionString = "";
        DatabaseName = "";
        await LivePostgresServer.DropDatabaseAsync(connectionString);
    }
}
