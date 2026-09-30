using System.Globalization;
using CoffeePeek.Testing;
using FluentAssertions;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

/// <summary>
/// Live self-tests of the harness: preflight, create/drop, and both lock-wait probes against a real
/// blocked session. Every gate test that uses a probe is only trustworthy once these pass, so they
/// run first (plan 05-08 after the non-production attestation, and always in the 05-07 gate run).
/// With COFFEEPEEK_LIVE_PG unset every test here reports Skipped.
/// </summary>
[Trait("Category", LivePostgresServer.Category)]
public sealed class LivePostgresHarnessTests
{
    // Not a production key (Shops 7402100101, Account 7402100401); each test uses its own database anyway.
    private const long ProbeKey = 7402100999;
    private const string ProbeLockSql = "SELECT pg_advisory_xact_lock(7402100999)";

    // The throwaway connection's lock_timeout: a waiter still blocked after this is a harness bug.
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    public LivePostgresHarnessTests()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
    }

    [Fact]
    public async Task Server_MeetsPrerequisitesAndReportsVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = LivePostgresServer.ParseAdminConnectionString(
            Environment.GetEnvironmentVariable(LivePostgresServer.EnvVar));
        var adminConnectionString = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Pooling = false }.ConnectionString;

        var version = await LivePostgresServer.ServerVersionAsync(adminConnectionString, ct);
        TestContext.Current.TestOutputHelper?.WriteLine($"LIVE-PG server_version={version}");

        await using var connection = await LivePostgresServer.OpenAsync(adminConnectionString, ct);

        var versionNum = int.Parse((string)(await ScalarAsync(connection, "SHOW server_version_num", ct))!,
            CultureInfo.InvariantCulture);
        versionNum.Should().BeGreaterThanOrEqualTo(170000, "the gate targets PostgreSQL 17");

        var trgm = (long)(await ScalarAsync(connection,
            "SELECT count(*) FROM pg_available_extensions WHERE name = 'pg_trgm'", ct))!;
        trgm.Should().BeGreaterThan(0, "Shops migration Update2025 creates the pg_trgm extension");

        bool isSuperuser, canCreateDb;
        await using (var role = new NpgsqlCommand(
                         "SELECT rolsuper, rolcreatedb FROM pg_roles WHERE rolname = current_user", connection))
        await using (var reader = await role.ExecuteReaderAsync(ct))
        {
            (await reader.ReadAsync(ct)).Should().BeTrue();
            isSuperuser = reader.GetBoolean(0);
            canCreateDb = reader.GetBoolean(1);
        }

        (isSuperuser || canCreateDb).Should().BeTrue("the role must be able to create throwaway databases");
        if (isSuperuser)
            LivePostgresServer.SuperuserAllowed(Environment.GetEnvironmentVariable(LivePostgresServer.AllowSuperuserEnvVar))
                .Should().BeTrue("a superuser role is refused unless explicitly allowed (D-03)");
    }

    [Fact]
    public async Task CreateDatabase_IsUtf8AndOwned_AndDropRemovesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await LivePostgresServer.CreateDatabaseAsync("harness", ct);
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        var dropped = false;
        try
        {
            LivePostgresServer.IsOwnedDatabaseName(name).Should().BeTrue();

            await using (var connection = await LivePostgresServer.OpenAsync(connectionString, ct))
            await using (var check = new NpgsqlCommand(
                             """
                             SELECT pg_encoding_to_char(d.encoding), r.rolname = current_user
                             FROM pg_database d JOIN pg_roles r ON r.oid = d.datdba
                             WHERE d.datname = current_database()
                             """, connection))
            await using (var reader = await check.ExecuteReaderAsync(ct))
            {
                (await reader.ReadAsync(ct)).Should().BeTrue();
                reader.GetString(0).Should().Be("UTF8");
                reader.GetBoolean(1).Should().BeTrue("the throwaway database is owned by the live-test role");
            }

            await LivePostgresServer.DropDatabaseAsync(connectionString);
            dropped = true;

            var admin = LivePostgresServer.ParseAdminConnectionString(
                Environment.GetEnvironmentVariable(LivePostgresServer.EnvVar));
            var adminConnectionString = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Pooling = false }.ConnectionString;
            await using var adminConnection = await LivePostgresServer.OpenAsync(adminConnectionString, ct);
            await using var exists = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", adminConnection);
            exists.Parameters.AddWithValue("name", name);
            ((long)(await exists.ExecuteScalarAsync(ct))!).Should().Be(0, "DropDatabaseAsync must remove the database");
        }
        finally
        {
            if (!dropped)
                await LivePostgresServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task AdvisoryWaiterProbe_ObservesBlockedSession()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await LivePostgresServer.CreateDatabaseAsync("harness", ct);
        Task? waiter = null;
        try
        {
            var holder = await LivePostgresServer.OpenAsync(connectionString, ct);
            await using (holder)
            {
                var holderTx = await holder.BeginTransactionAsync(ct);
                await using (holderTx)
                {
                    await ExecuteAsync(holder, holderTx, ProbeLockSql, ct);

                    waiter = Task.Run(async () =>
                    {
                        await using var connection = await LivePostgresServer.OpenAsync(connectionString, ct);
                        await using var tx = await connection.BeginTransactionAsync(ct);
                        await ExecuteAsync(connection, tx, ProbeLockSql, ct);
                        await tx.CommitAsync(ct);
                    }, ct);

                    await LivePostgresServer.WaitForAdvisoryWaitersAsync(connectionString, ProbeKey, expected: 1, ct);
                    waiter.IsCompleted.Should().BeFalse("the second session must be blocked on the held advisory lock");

                    await holderTx.CommitAsync(ct);
                }
            }

            // Holder released: the waiter must acquire the lock well within lock_timeout.
            await waiter.WaitAsync(LockTimeout, ct);
        }
        finally
        {
            // The holder is already disposed here on every path, so the waiter can finish; await it
            // so no task outlives the test, then drop the database.
            await SettleAsync(waiter);
            await LivePostgresServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task TransactionIdProbe_ObservesUniqueInsertWait()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await LivePostgresServer.CreateDatabaseAsync("harness", ct);
        Task? waiter = null;
        try
        {
            await using (var setup = await LivePostgresServer.OpenAsync(connectionString, ct))
                await ExecuteAsync(setup, null, "CREATE TABLE live_probe (k int PRIMARY KEY)", ct);

            var holder = await LivePostgresServer.OpenAsync(connectionString, ct);
            await using (holder)
            {
                var holderTx = await holder.BeginTransactionAsync(ct);
                await using (holderTx)
                {
                    await ExecuteAsync(holder, holderTx, "INSERT INTO live_probe (k) VALUES (1)", ct);

                    waiter = Task.Run(async () =>
                    {
                        await using var connection = await LivePostgresServer.OpenAsync(connectionString, ct);
                        await ExecuteAsync(connection, null, "INSERT INTO live_probe (k) VALUES (1)", ct);
                    }, ct);

                    await LivePostgresServer.WaitForLockWaitsAsync(connectionString, "transactionid", expected: 1, ct);
                    waiter.IsCompleted.Should().BeFalse("the second insert must wait on the uncommitted duplicate key");

                    await holderTx.CommitAsync(ct);
                }
            }

            var act = () => waiter.WaitAsync(LockTimeout, ct);
            (await act.Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }
        finally
        {
            await SettleAsync(waiter);
            await LivePostgresServer.DropDatabaseAsync(connectionString);
        }
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(ct);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Awaits a background session during cleanup. Its outcome was already asserted on the success
    /// path; on a failure path the primary exception is the one that matters, so this one is ignored.
    /// </summary>
    private static async Task SettleAsync(Task? task)
    {
        if (task is null)
            return;

        try
        {
            await task.WaitAsync(LockTimeout, CancellationToken.None);
        }
        catch (Exception)
        {
            // Intentionally ignored: see summary.
        }
    }
}
