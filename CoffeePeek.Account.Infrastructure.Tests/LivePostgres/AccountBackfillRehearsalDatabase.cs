using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

public sealed class AccountBackfillRehearsalDatabase : AccountLiveDatabase
{
    protected override string Service => "accountbackfill";
    protected override string PreFeatureMigration => "20260921104154_AddAccountDeletionConfirmationSecurity";
    public string[] BeforeMigrations { get; private set; } = [];
    public bool RegistryExistedBefore { get; private set; }
    public Dictionary<Guid, string> OriginalUsernames { get; } = [];
    public Guid[] IvanIds { get; private set; } = [];
    public Guid AdminId { get; private set; }
    public Guid BlockedId { get; private set; }
    public Guid DeletedId { get; private set; }

    protected override async Task SeedLegacyAsync(AccountDbContext db, CancellationToken ct)
    {
        BeforeMigrations = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        await using (var connection = await LivePostgresServer.OpenAsync(ConnectionString, ct))
        await using (var command = new NpgsqlCommand("SELECT to_regclass('public.\"UserPublicAddresses\"') IS NOT NULL", connection))
            RegistryExistedBefore = (bool)(await command.ExecuteScalarAsync(ct))!;
        var role = Role.Create("User");
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        var users = new[] { "Ivan", "ivan", "Иван", "admin", "Boris.Blocked", "Dmitry.Deleted" }
            .Select(name => User.Register(Guid.NewGuid().ToString("N") + "@example.com", name, "hash", role)).ToArray();
        users[4].SetBlocked(true);
        users[5].SetSoftDelete();
        // Seed the old schema directly: the current EF model includes the later cooldown column.
        foreach (var user in users)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Users" ("Id", "Username", "Email", "PasswordHash", "EmailConfirmed", "IsSoftDelete", "IsBlocked",
                    "CreatedAtUtc", "CheckInCount", "ReviewCount", "AddedShopsCount", "StatisticUpdatedAtUtc")
                VALUES ({user.Id}, {user.Username.Value}, {user.Credentials.Email.Value}, {"hash"}, {false}, {user.IsSoftDelete}, {user.IsBlocked},
                    {DateTime.UtcNow}, {0}, {0}, {0}, {DateTime.UtcNow})
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"UserRoles\" (\"UserId\", \"RoleId\") VALUES ({user.Id}, {role.Id})", ct);
        }
        foreach (var user in users) OriginalUsernames.Add(user.Id, user.Username.Value);
        IvanIds = users.Take(3).Select(x => x.Id).ToArray();
        AdminId = users[3].Id;
        BlockedId = users[4].Id;
        DeletedId = users[5].Id;
    }
}
