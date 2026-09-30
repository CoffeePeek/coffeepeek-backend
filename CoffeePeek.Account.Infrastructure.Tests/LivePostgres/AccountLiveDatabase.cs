using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

public class AccountLiveDatabase : LiveDatabase<AccountDbContext>
{
    protected override string Service => "account";
    protected override AccountDbContext Create(DbContextOptions<AccountDbContext> options, bool enabled) =>
        new(options, Microsoft.Extensions.Options.Options.Create(new PublicAddressOptions { Enabled = enabled }));

    protected override async Task AfterMigrateAsync(AccountDbContext db, CancellationToken ct)
    {
        if (!await db.Roles.AnyAsync(ct))
        {
            db.Roles.Add(Role.Create("User"));
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<Guid> RegisterAsync(string username, bool enabled, CancellationToken ct, string? email = null)
    {
        await using var db = Context(enabled);
        var role = await db.Roles.SingleAsync(r => r.Name == "User", ct);
        var user = User.Register(email ?? Guid.NewGuid().ToString("N") + "@example.com", username, "hash", role);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }
}
