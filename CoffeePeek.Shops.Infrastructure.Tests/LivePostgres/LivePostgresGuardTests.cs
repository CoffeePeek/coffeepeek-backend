using System.Net.Sockets;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

/// <summary>
/// Server-free tests of every live-PostgreSQL safety guard. These are deliberately NOT in the
/// LivePostgres category: they need no server, so they run in CI on every build and prove that
/// the harness cannot touch a database it did not create, and cannot leak a secret.
/// </summary>
public class LivePostgresGuardTests
{
    private const string ValidName = "cp_live_shops_20260929t101530_1a2b3c4d";
    private const string ValidAdmin = "Host=db.example;Port=5432;Database=postgres;Username=cp_live_tests;SSL Mode=Prefer";
    private const string Secret = "hunter2-secret";
    private const string SecretHost = "secret-host.example";

    private static readonly DateTime Stamp = new(2026, 9, 29, 10, 15, 30, DateTimeKind.Utc);

    public static TheoryData<string> RejectedNames =>
    [
        "",
        "postgres",
        "template0",
        "template1",
        "cpshopsdb",
        "cpaccountdb",
        "cp_live_",
        "cp_live_shops",
        "cp_live_Shops_20260929t101530_1a2b3c4d",
        "cp_live_shops_20260929t101530_1A2B3C4D",
        "cp_live_shops_20260929t101530_1a2b3c4",
        "cp_live_shops_20260929t101530_1a2b3c4d5",
        "cp_live_shops_20260929t101530_1a2b3c4d_x",
        "cp_live_shops_2026092t101530_1a2b3c4d",
        "cp_live_shops-1_20260929t101530_1a2b3c4d",
        ValidName + "\n",
        " " + ValidName,
        ValidName + "\"; DROP DATABASE cpshopsdb; --",
        // Pattern-valid but longer than PostgreSQL's 63-byte identifier limit: the server would
        // silently truncate it, so the name dropped would not be the name validated.
        "cp_live_" + new string('a', 40) + "_20260929t101530_1a2b3c4d",
    ];

    // ---- NewDatabaseName -------------------------------------------------------------------

    [Fact]
    public void NewDatabaseName_EmbedsServiceAndUtcStamp_AndIsOwned()
    {
        var first = LivePostgresServer.NewDatabaseName("shops", Stamp);
        var second = LivePostgresServer.NewDatabaseName("shops", Stamp);

        first.Should().StartWith("cp_live_shops_20260929t101530_");
        first.Should().MatchRegex(@"\Acp_live_shops_20260929t101530_[0-9a-f]{8}\z");
        second.Should().StartWith("cp_live_shops_20260929t101530_");
        second.Should().NotBe(first, "the random suffix keeps concurrent runs apart");

        LivePostgresServer.IsOwnedDatabaseName(first).Should().BeTrue();
        LivePostgresServer.IsOwnedDatabaseName(second).Should().BeTrue();
        first.Length.Should().BeLessThanOrEqualTo(63);
        second.Length.Should().BeLessThanOrEqualTo(63);
    }

    [Fact]
    public void NewDatabaseName_LongestSupportedService_IsOwnedAndFits()
    {
        var name = LivePostgresServer.NewDatabaseName("accountbackfill", Stamp);

        LivePostgresServer.IsOwnedDatabaseName(name).Should().BeTrue();
        name.Length.Should().BeLessThanOrEqualTo(63);
    }

    [Theory]
    [InlineData("Shops")]
    [InlineData("shops-1")]
    [InlineData("")]
    [InlineData("shops;drop")]
    [InlineData("shops ")]
    [InlineData("shops\n")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcde")] // 31 letters: the name would exceed 63 bytes
    public void NewDatabaseName_InvalidService_Throws(string service)
    {
        var act = () => LivePostgresServer.NewDatabaseName(service, Stamp);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NewDatabaseName_NullService_Throws()
    {
        var act = () => LivePostgresServer.NewDatabaseName(null!, Stamp);

        act.Should().Throw<ArgumentException>();
    }

    // ---- IsOwnedDatabaseName / QuoteOwnedDatabase -----------------------------------------

    [Fact]
    public void IsOwnedDatabaseName_AcceptsGeneratedShape() =>
        LivePostgresServer.IsOwnedDatabaseName(ValidName).Should().BeTrue();

    [Theory]
    [MemberData(nameof(RejectedNames))]
    public void IsOwnedDatabaseName_RejectsEverythingElse(string name) =>
        LivePostgresServer.IsOwnedDatabaseName(name).Should().BeFalse();

    [Fact]
    public void IsOwnedDatabaseName_Null_IsFalse() =>
        LivePostgresServer.IsOwnedDatabaseName(null).Should().BeFalse();

    [Fact]
    public void QuoteOwnedDatabase_OwnedName_IsDoubleQuoted() =>
        LivePostgresServer.QuoteOwnedDatabase(ValidName).Should().Be("\"" + ValidName + "\"");

    [Theory]
    [MemberData(nameof(RejectedNames))]
    public void QuoteOwnedDatabase_RejectedName_Throws(string name)
    {
        var act = () => LivePostgresServer.QuoteOwnedDatabase(name);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void QuoteOwnedDatabase_Null_Throws()
    {
        var act = () => LivePostgresServer.QuoteOwnedDatabase(null!);

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- ParseAdminConnectionString --------------------------------------------------------

    [Fact]
    public void ParseAdminConnectionString_MaintenanceDbWithoutPassword_IsAccepted()
    {
        var builder = LivePostgresServer.ParseAdminConnectionString(ValidAdmin);

        builder.Host.Should().Be("db.example");
        builder.Port.Should().Be(5432);
        builder.Database.Should().Be("postgres");
        builder.Username.Should().Be("cp_live_tests");
        builder.Password.Should().BeNullOrEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Host=db.example;Username=cp_live_tests")]
    [InlineData("Host=db.example;Database=template1;Username=cp_live_tests")]
    [InlineData("Host=db.example;Database=template0;Username=cp_live_tests")]
    [InlineData("Host=db.example;Database=cpshopsdb;Username=cp_live_tests")]
    [InlineData("Host=db.example;Database=cpaccountdb;Username=cp_live_tests")]
    [InlineData("Host=db.example;Database=Postgres;Username=cp_live_tests")]
    [InlineData("Host=db.example;Database=postgres;Username=cp_live_tests;Password=x")]
    [InlineData("Host=db.example;Database=postgres;Username=cp_live_tests;PWD=x")]
    public void ParseAdminConnectionString_Refused(string? value)
    {
        var act = () => LivePostgresServer.ParseAdminConnectionString(value);

        act.Should().Throw<InvalidOperationException>().Which.InnerException.Should().BeNull();
    }

    [Fact]
    public void ParseAdminConnectionString_PasswordRefusal_NamesPgpassFix()
    {
        var act = () => LivePostgresServer.ParseAdminConnectionString(ValidAdmin + ";Password=x");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("pgpass").And.Contain("PGPASSFILE");
    }

    [Theory]
    [InlineData("Password=" + Secret + ";Host=" + SecretHost)]
    [InlineData("Host=" + SecretHost + ";Database=postgres;Username=cp_live_tests;Password=" + Secret)]
    [InlineData("Host=" + SecretHost + ";Database=" + Secret + ";Username=cp_live_tests")]
    [InlineData("Host=" + SecretHost + ";Password='" + Secret)]
    [InlineData("Host=" + SecretHost + ";" + Secret)]
    [InlineData("Host=" + SecretHost + ";Database=postgres;" + Secret + "=1")]
    public void ParseAdminConnectionString_NeverLeaksInputText(string value)
    {
        var act = () => LivePostgresServer.ParseAdminConnectionString(value);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.InnerException.Should().BeNull();
        ex.Message.Should().NotContain(Secret).And.NotContain(SecretHost);
        ex.ToString().Should().NotContain(Secret).And.NotContain(SecretHost);
    }

    // ---- SuperuserAllowed ------------------------------------------------------------------

    [Theory]
    [InlineData("1", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData("yes", false)]
    [InlineData(" 1", false)]
    [InlineData("1 ", false)]
    [InlineData("11", false)]
    public void SuperuserAllowed_OnlyExactlyOne(string? value, bool expected) =>
        LivePostgresServer.SuperuserAllowed(value).Should().Be(expected);

    // ---- ThrowawayConnectionString ---------------------------------------------------------

    [Fact]
    public void ThrowawayConnectionString_TargetsThrowawayDbWithLockTimeout()
    {
        var admin = LivePostgresServer.ParseAdminConnectionString(ValidAdmin);

        var result = new NpgsqlConnectionStringBuilder(LivePostgresServer.ThrowawayConnectionString(admin, ValidName));

        result.Database.Should().Be(ValidName);
        result.Options.Should().Contain("lock_timeout=30s");
        result.ApplicationName.Should().Be("coffeepeek-live-tests");
        result.Host.Should().Be("db.example");
        result.Port.Should().Be(5432);
        result.Username.Should().Be("cp_live_tests");
        result.Password.Should().BeNullOrEmpty();
        admin.Database.Should().Be("postgres", "the admin builder must not be mutated");
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("cpshopsdb")]
    [InlineData("cp_live_shops")]
    public void ThrowawayConnectionString_NonOwnedName_Throws(string name)
    {
        var admin = LivePostgresServer.ParseAdminConnectionString(ValidAdmin);

        var act = () => LivePostgresServer.ThrowawayConnectionString(admin, name);

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- IsSweepCandidate ------------------------------------------------------------------

    [Fact]
    public void IsSweepCandidate_OwnedAndOlderThanSixHours_IsTrue()
    {
        var now = Stamp.AddHours(7);

        LivePostgresServer.IsSweepCandidate(ValidName, now).Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(-1)] // a stamp in the future (clock skew between runners) is never swept
    public void IsSweepCandidate_Recent_IsFalse(int hoursAfterStamp) =>
        LivePostgresServer.IsSweepCandidate(ValidName, Stamp.AddHours(hoursAfterStamp)).Should().BeFalse();

    [Theory]
    [InlineData("cpshopsdb")]
    [InlineData("postgres")]
    [InlineData("cp_live_shops_20260929t101530_1a2b3c4d_x")]
    [InlineData("cp_live_shops_20260929t101530_1A2B3C4D")]
    public void IsSweepCandidate_NotOwned_IsFalse(string name) =>
        LivePostgresServer.IsSweepCandidate(name, Stamp.AddDays(30)).Should().BeFalse();

    [Theory]
    [InlineData("cp_live_shops_20261329t101530_1a2b3c4d")] // month 13
    [InlineData("cp_live_shops_20260231t101530_1a2b3c4d")] // 31 February
    [InlineData("cp_live_shops_20260929t251530_1a2b3c4d")] // hour 25
    public void IsSweepCandidate_ImpossibleStamp_IsFalse(string name) =>
        LivePostgresServer.IsSweepCandidate(name, Stamp.AddDays(365)).Should().BeFalse();

    // ---- ValidateRegistryTable -------------------------------------------------------------

    [Theory]
    [InlineData("CoffeeShopPublicAddresses")]
    [InlineData("RoasterPublicAddresses")]
    [InlineData("CityPublicAddresses")]
    [InlineData("CoffeeZonePublicAddresses")]
    [InlineData("UserPublicAddresses")]
    public void ValidateRegistryTable_RegistryTables_ReturnedUnchanged(string table) =>
        LivePostgresServer.ValidateRegistryTable(table).Should().Be(table);

    [Theory]
    [InlineData("Users")]
    [InlineData("PublicAddressAudit")]
    [InlineData("PublicAddresses")]
    [InlineData("coffeeShopPublicAddresses")]
    [InlineData("CityPublicAddresses\n")]
    [InlineData("City_PublicAddresses")]
    [InlineData("CityPublicAddresses\"; DROP TABLE x; --")]
    [InlineData("")]
    public void ValidateRegistryTable_AnythingElse_Throws(string table)
    {
        var act = () => LivePostgresServer.ValidateRegistryTable(table);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateRegistryTable_Null_Throws()
    {
        var act = () => LivePostgresServer.ValidateRegistryTable(null!);

        act.Should().Throw<ArgumentException>();
    }

    // ---- FindPostgres ----------------------------------------------------------------------

    [Fact]
    public void FindPostgres_UnwrapsConflictAndDbUpdateException()
    {
        var pg = new PostgresException("m", "ERROR", "ERROR", "23505");
        var ex = new ConflictException("x", new DbUpdateException("y", pg));

        LivePostgresServer.FindPostgres(ex).Should().BeSameAs(pg);
    }

    [Fact]
    public void FindPostgres_PostgresExceptionItself_IsReturned()
    {
        var pg = new PostgresException("m", "ERROR", "ERROR", "23503");

        LivePostgresServer.FindPostgres(pg).Should().BeSameAs(pg);
    }

    [Fact]
    public void FindPostgres_NoPostgresException_IsNull() =>
        LivePostgresServer.FindPostgres(new InvalidOperationException("x")).Should().BeNull();

    [Fact]
    public void FindPostgres_Null_IsNull() =>
        LivePostgresServer.FindPostgres(null).Should().BeNull();

    // ---- DropDatabaseAsync guard runs before any connection attempt ------------------------

    [Theory]
    [InlineData("Host=invalid.invalid;Database=cpshopsdb;Username=u;Timeout=1")]
    [InlineData("Host=invalid.invalid;Database=postgres;Username=u;Timeout=1")]
    [InlineData("Host=invalid.invalid;Username=u;Timeout=1")]
    [InlineData("Host=invalid.invalid;Database=cp_live_shops_20260929t101530_1a2b3c4d_x;Username=u;Timeout=1")]
    public async Task DropDatabaseAsync_NonOwnedDatabase_RefusedBeforeConnecting(string connectionString)
    {
        var act = () => LivePostgresServer.DropDatabaseAsync(connectionString);

        var ex = (await act.Should().ThrowAsync<Exception>()).Which;
        ex.Should().BeOfType<InvalidOperationException>(
            "the owned-name guard must refuse before any network I/O (no NpgsqlException/SocketException)");
        ex.Should().NotBeAssignableTo<NpgsqlException>();
        ex.Should().NotBeAssignableTo<SocketException>();
        ex.InnerException.Should().BeNull();
        // A wrapped connect failure is also an InvalidOperationException, so the message proves
        // the refusal came from the owned-name guard and not from a connection attempt.
        ex.Message.Should().StartWith("Refusing to touch a database that is not a cp_live_* throwaway database");
    }

    [Fact]
    public void Constants_MatchProductionLockKeysAndGateNames()
    {
        LivePostgresServer.Category.Should().Be("LivePostgres");
        LivePostgresServer.EnvVar.Should().Be("COFFEEPEEK_LIVE_PG");
        LivePostgresServer.AllowSuperuserEnvVar.Should().Be("COFFEEPEEK_LIVE_PG_ALLOW_SUPERUSER");
        LivePostgresServer.ShopsLockKey.Should().Be(7402100101);
        LivePostgresServer.AccountLockKey.Should().Be(7402100401);
        LivePostgresServer.SkipReason.Should().Contain(LivePostgresServer.EnvVar);
    }
}
