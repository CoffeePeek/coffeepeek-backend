using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopsLiveSchemaTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public ShopsLiveSchemaTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task Migrations_ApplyCleanlyToEmptyDatabase()
    {
        await using var db = fixture.Context(false);
        (await db.Database.GetPendingMigrationsAsync(Ct)).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(Ct)).Should().Equal(db.Database.GetMigrations());
        db.Database.GetMigrations().Should().Contain([
            "20260927112559_AddPublicAddressRegistries", "20260928072406_AddPublicAddressAudit",
            "20260928082712_AddCityAndZonePublicAddresses"]);
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Theory]
    [InlineData(PublicAddressKind.CoffeeShop)]
    [InlineData(PublicAddressKind.Roaster)]
    [InlineData(PublicAddressKind.City)]
    [InlineData(PublicAddressKind.CoffeeZone)]
    [InlineData(PublicAddressKind.CoffeeBean)]
    [InlineData(PublicAddressKind.Equipment)]
    [InlineData(PublicAddressKind.BrewMethod)]
    [InlineData(PublicAddressKind.ShopTag)]
    public async Task RegistryConstraints_RejectInvalidRows(PublicAddressKind kind)
    {
        var owner = await fixture.AddOwnerAsync(kind, "schema owner one", false, Ct);
        var second = await fixture.AddOwnerAsync(kind, "schema owner two", false, Ct);
        var table = ShopsLiveDatabase.Tables(kind).Registry;
        await using var connection = await LivePostgresServer.OpenAsync(fixture.ConnectionString, Ct);
        var now = DateTime.UtcNow;
        Task Insert(Guid? id, string slug, int state = 1, long revision = 1, DateTime? updated = null) =>
            LivePostgresServer.InsertRegistryRowAsync(connection, null, table, id, slug, state, revision, now, updated ?? now, Ct);
        await Insert(owner, "schema-ok");
        await Insert(owner, "schema-alias", 2);
        await Insert(null, "schema-tomb", 3);
        foreach (var slug in new[] { "Bad Slug", "a--b", "-a", "Minsk", "" })
            await Reject(() => Insert(second, slug), "23514", "CK_" + table + "_Slug");
        await Reject(() => Insert(second, new string('a', 101)), "22001", null);
        await Reject(() => Insert(null, "schema-null"), "23514", "CK_" + table + "_Lifecycle");
        await Reject(() => Insert(second, "schema-owned-tomb", 3), "23514", "CK_" + table + "_Lifecycle");
        await Reject(() => Insert(second, "schema-state", 0), "23514", "CK_" + table + "_Lifecycle");
        await Reject(() => Insert(second, "schema-revision", revision: 0), "23514", "CK_" + table + "_Revision");
        await Reject(() => Insert(second, "schema-time", updated: now.AddMinutes(-1)), "23514", "CK_" + table + "_Timestamps");
        await Reject(() => Insert(null, "schema-tomb", 3), "23505", "IX_" + table + "_Slug");
        await Reject(() => Insert(owner, "schema-second"), "23505", "IX_" + table + "_OwnerId");
    }

    [Fact]
    public async Task AuditConstraints_AcceptFourKindsAndRejectInvalidKindRevisionAndDuplicate()
    {
        await using var connection = await LivePostgresServer.OpenAsync(fixture.ConnectionString, Ct);
        var owner = Guid.NewGuid();
        async Task Insert(int kind, long revision)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO "PublicAddressAudit" ("Id","Kind","OwnerId","Revision","Action","Reason","AtUtc")
                VALUES (@id,@kind,@owner,@revision,'initialize','live gate',@now)
                """, connection);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("kind", kind);
            command.Parameters.AddWithValue("owner", owner);
            command.Parameters.AddWithValue("revision", revision);
            command.Parameters.AddWithValue("now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(Ct);
        }
        foreach (var kind in new[] { 1, 2, 3, 4, 5, 6, 7, 8 }) await Insert(kind, 1);
        foreach (var kind in new[] { 0, 9 }) await Reject(() => Insert(kind, 2), "23514", "CK_PublicAddressAudit_Kind");
        await Reject(() => Insert(1, 0), "23514", "CK_PublicAddressAudit_Revision");
        await Reject(() => Insert(3, 1), "23505", "IX_PublicAddressAudit_Kind_OwnerId_Revision");
    }

    [Theory]
    [InlineData(PublicAddressKind.CoffeeShop)]
    [InlineData(PublicAddressKind.Roaster)]
    [InlineData(PublicAddressKind.City)]
    [InlineData(PublicAddressKind.CoffeeZone)]
    [InlineData(PublicAddressKind.CoffeeBean)]
    [InlineData(PublicAddressKind.Equipment)]
    [InlineData(PublicAddressKind.BrewMethod)]
    [InlineData(PublicAddressKind.ShopTag)]
    public async Task OwnerDelete_RestrictsRawDeleteAndRetiresWithFlagOff(PublicAddressKind kind)
    {
        var id = await fixture.AddOwnerAsync(kind, "delete " + kind, true, Ct);
        var (table, owners, _, ownerType) = ShopsLiveDatabase.Tables(kind);
        await using var db = fixture.Context();
        var original = await new PublicAddressService(db).GetByIdsAsync(kind, [id], Ct);
        original.Should().ContainSingle();
        var renamed = await new PublicAddressService(db).ChangeAsync(kind, id,
            new(original[0].Slug + "-renamed", 1, "live delete aliases"), Guid.NewGuid(), Ct);
        await using var connection = await LivePostgresServer.OpenAsync(fixture.ConnectionString, Ct);
        async Task Delete()
        {
            await using var command = new NpgsqlCommand($"DELETE FROM \"{owners}\" WHERE \"Id\"=@id", connection);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync(Ct);
        }
        await Reject(Delete, "23503", $"FK_{table}_{owners}_OwnerId");
        await using (var disabled = fixture.Context(false))
        {
            disabled.Remove((await disabled.FindAsync(ownerType, [id], Ct))!);
            await disabled.SaveChangesAsync(Ct);
        }
        await using var check = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\" WHERE \"Slug\"=ANY(@slugs) AND \"State\"=3 AND \"OwnerId\" IS NULL AND \"Revision\"=3", connection);
        check.Parameters.AddWithValue("slugs", new[] { original[0].Slug, renamed.Slug });
        ((long)(await check.ExecuteScalarAsync(Ct))!).Should().Be(2);
        (await db.PublicAddressAudit.AsNoTracking().CountAsync(a => a.OwnerId == id && a.Action == "retire", Ct)).Should().Be(1);
    }

    private static async Task Reject(Func<Task> action, string state, string? constraint)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(action);
        error.SqlState.Should().Be(state);
        if (constraint is not null) error.ConstraintName.Should().Be(constraint);
    }
}
