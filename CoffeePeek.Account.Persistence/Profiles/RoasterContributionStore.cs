using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Persistence.Profiles;

public sealed class RoasterContributionStore(AccountDbContext db) : IRoasterContributionStore
{
    public async Task CreditAsync(Guid userId, Guid roasterId, CancellationToken ct)
    {
        if (userId == Guid.Empty || roasterId == Guid.Empty) return;
        if (db.Database.IsNpgsql())
        {
            // The ledger insert and increment are one statement/transaction. A replay, including a fresh envelope,
            // cannot increment twice; row-level UPDATE locking also prevents lost increments for distinct roasters.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                WITH credited AS (
                    INSERT INTO "UserRoasterCredits" ("RoasterId", "UserId", "CreatedAtUtc")
                    SELECT {roasterId}, "Id", {DateTime.UtcNow} FROM "Users"
                    WHERE "Id" = {userId} AND NOT "IsSoftDelete"
                    ON CONFLICT ("RoasterId") DO NOTHING
                    RETURNING "UserId"
                )
                UPDATE "Users" SET "AddedRoastersCount" = "AddedRoastersCount" + 1,
                    "StatisticUpdatedAtUtc" = {DateTime.UtcNow}
                WHERE "Id" IN (SELECT "UserId" FROM credited)
                """, ct);
            return;
        }

        if (await db.UserRoasterCredits.AnyAsync(x => x.RoasterId == roasterId, ct)) return;
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && !x.IsSoftDelete, ct);
        if (user is null) return;
        db.UserRoasterCredits.Add(new UserRoasterCredit(roasterId, userId));
        user.Statistics.IncrementAddedRoasters();
        await db.SaveChangesAsync(ct);
    }
}
