using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.Persistence;

public sealed class UsernameChangeHistoryTests
{
    [Fact]
    public async Task Reload_PreservesWeeklyLimit()
    {
        var options = new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AccountDbContext(options);
        var user = User.CreateExternal("history@example.com", "Google", "test", "Original");
        var now = DateTime.UtcNow;
        user.UpdateUsername(Username.Create("Second"), now);
        user.UpdateUsername(Username.Create("Third"), now);
        user.UpdateUsername(Username.Create("Fourth"), now);
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var saved = await db.Users.SingleAsync(TestContext.Current.CancellationToken);
        var rename = () => saved.UpdateUsername(Username.Create("Fifth"), now.AddDays(1));

        saved.UsernameChangeHistoryUtc.Should().Equal(now, now, now);
        rename.Should().Throw<ConflictException>();
    }

    [Fact]
    public async Task ConcurrentRename_WithIdenticalTimestamp_RejectsStaleHistory()
    {
        var options = new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var first = new AccountDbContext(options);
        var user = User.CreateExternal("concurrent@example.com", "Google", "test", "Original");
        var now = DateTime.UtcNow;
        user.UpdateUsername(Username.Create("Second"), now);
        first.Users.Add(user);
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var stale = new AccountDbContext(options);
        var staleUser = await stale.Users.SingleAsync(TestContext.Current.CancellationToken);
        user.UpdateUsername(Username.Create("Third"), now);
        staleUser.UpdateUsername(Username.Create("Fourth"), now);
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        var save = () => stale.SaveChangesAsync(TestContext.Current.CancellationToken);

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public void PostgresModel_StoresHistoryAsTimestampArrayWithConcurrencyCheck()
    {
        using var db = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var property = db.Model.FindEntityType(typeof(User))!
            .FindProperty(nameof(User.UsernameChangeHistoryUtc))!;

        property.GetColumnType().Should().Be("timestamp with time zone[]");
        property.GetDefaultValueSql().Should().Be("'{}'::timestamp with time zone[]");
        property.IsConcurrencyToken.Should().BeTrue();
        property.IsNullable.Should().BeFalse();
    }
}
