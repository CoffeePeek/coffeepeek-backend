using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

public sealed class AccountSessionMigrationDatabase : AccountLiveDatabase
{
    protected override string Service => "accountsessionbackfill";
    protected override string PreFeatureMigration => "20261026072407_AddActivityLeaderboard";

    protected override async Task SeedLegacyAsync(AccountDbContext db, CancellationToken ct)
    {
        var user = User.CreateExternal("legacy-session@example.com", "Google", "legacy-session");
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        foreach (var (value, daysAgo, revoked) in new[]
                 {
                     ("legacy-active", 2, false),
                     ("legacy-expired", 100, false),
                     ("legacy-revoked", 2, true)
                 })
        {
            var id = Guid.NewGuid();
            var created = DateTime.UtcNow.AddDays(-daysAgo);
            var expiry = created.AddDays(7);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "RefreshTokens"
                    ("Id", "UserId", "Token", "DeviceName", "IpAddress", "CreatedAtUtc", "ExpiryDate", "IsRevoked")
                VALUES ({id}, {user.Id}, {value}, 'legacy-device', '127.0.0.1', {created}, {expiry}, {revoked})
                """, ct);
        }
    }
}
