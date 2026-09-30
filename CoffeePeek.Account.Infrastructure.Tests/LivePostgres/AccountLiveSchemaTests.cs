using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class AccountLiveSchemaTests : IClassFixture<AccountLiveDatabase>
{
    private readonly AccountLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public AccountLiveSchemaTests(AccountLiveDatabase fixture)
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
        db.Database.GetMigrations().Should().Contain("20260928134755_AddUserPublicAddresses");
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public async Task RegistryConstraints_RejectInvalidRows()
    {
        var owner = await fixture.RegisterAsync("Schema.One", false, Ct);
        var second = await fixture.RegisterAsync("Schema.Two", false, Ct);
        await using var connection = await LivePostgresServer.OpenAsync(fixture.ConnectionString, Ct);
        var now = DateTime.UtcNow;
        Task Insert(Guid? id, string slug, int state = 1, long revision = 1, DateTime? updated = null) =>
            LivePostgresServer.InsertRegistryRowAsync(connection, null, "UserPublicAddresses", id, slug, state, revision, now, updated ?? now, Ct);
        await Insert(owner, "schema-ok");
        await Insert(owner, "schema-alias", 2);
        await Insert(null, "schema-tomb", 3);
        foreach (var slug in new[] { "Bad Slug", "a--b", "-a", "Minsk", "" })
            await Reject(() => Insert(second, slug), "23514", "CK_UserPublicAddresses_Slug");
        await Reject(() => Insert(second, new string('a', 101)), "22001", null);
        await Reject(() => Insert(null, "schema-null"), "23514", "CK_UserPublicAddresses_Lifecycle");
        await Reject(() => Insert(second, "schema-owned-tomb", 3), "23514", "CK_UserPublicAddresses_Lifecycle");
        await Reject(() => Insert(second, "schema-state", 0), "23514", "CK_UserPublicAddresses_Lifecycle");
        await Reject(() => Insert(second, "schema-revision", revision: 0), "23514", "CK_UserPublicAddresses_Revision");
        await Reject(() => Insert(second, "schema-time", updated: now.AddMinutes(-1)), "23514", "CK_UserPublicAddresses_Timestamps");
        await Reject(() => Insert(null, "schema-tomb", 3), "23505", "IX_UserPublicAddresses_Slug");
        await Reject(() => Insert(owner, "schema-second"), "23505", "IX_UserPublicAddresses_OwnerId");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Delete_RestrictsRawDeleteAndRetiresAllAliases(bool hard, bool enabled)
    {
        var prefix = (hard ? "Hard" : "Soft") + (enabled ? "On" : "Off");
        var id = await fixture.RegisterAsync(prefix + ".One", true, Ct);
        await using (var rename = fixture.Context())
        {
            var user = await rename.Users.SingleAsync(x => x.Id == id, Ct);
            user.UpdateUsername(Username.Create(prefix + ".Two"));
            await rename.SaveChangesAsync(Ct);
        }
        await using var connection = await LivePostgresServer.OpenAsync(fixture.ConnectionString, Ct);
        async Task Delete()
        {
            await using var command = new NpgsqlCommand("DELETE FROM \"Users\" WHERE \"Id\"=@id", connection);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync(Ct);
        }
        await Reject(Delete, "23503", "FK_UserPublicAddresses_Users_OwnerId");
        await using var db = fixture.Context(enabled);
        var slugs = await db.UserPublicAddresses.Where(x => x.OwnerId == id).Select(x => x.Slug).ToArrayAsync(Ct);
        slugs.Should().HaveCount(2);
        var owner = await db.Users.SingleAsync(x => x.Id == id, Ct);
        if (hard) db.Users.Remove(owner); else owner.SetSoftDelete();
        await db.SaveChangesAsync(Ct);
        var rows = await db.UserPublicAddresses.AsNoTracking().Where(x => slugs.Contains(x.Slug)).ToArrayAsync(Ct);
        rows.Should().HaveCount(2).And.OnlyContain(x => x.State == PublicAddressState.Tombstone && x.OwnerId == null && x.Revision == 3);
    }

    private static async Task Reject(Func<Task> action, string state, string? constraint)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(action);
        error.SqlState.Should().Be(state);
        if (constraint is not null) error.ConstraintName.Should().Be(constraint);
    }
}
