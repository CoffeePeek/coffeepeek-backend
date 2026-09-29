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

using System.Globalization;
using System.Text.RegularExpressions;
using Npgsql;

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

    private static readonly TimeSpan SweepAge = TimeSpan.FromHours(6);

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

        NpgsqlConnection.ClearAllPools();

        await using var admin = await OpenMaintenanceAsync(target, CancellationToken.None);
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {quoted} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync(CancellationToken.None);
    }

    // ---- Internals ------------------------------------------------------------------------

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
    /// Opens a connection. A failure is rethrown with a fixed message that names only exception
    /// types, SqlState and socket error codes: Npgsql's own connect errors can contain the host.
    /// </summary>
    private static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken ct)
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
