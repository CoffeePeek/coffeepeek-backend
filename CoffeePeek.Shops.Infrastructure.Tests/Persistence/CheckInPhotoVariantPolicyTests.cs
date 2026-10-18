using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Infrastructure.Account;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CheckInPhotoVariantPolicyTests
{
    [Fact]
    public async Task VariantFactsCacheCannotRelaxOriginalOrPrivateAccessOrHideAndDeleteChecks()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var row = CheckIn.CreateHome(owner, "author", null, DateTime.UtcNow, DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, null, null, null);
        var photo = new CheckInPhoto("photo.jpg", "image/jpeg", "private/photo", 100, owner);
        row.AddPhotos([photo]); row.ChangeVisibility(CheckInVisibility.Public);
        row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, DateTime.UtcNow);
        db.CheckIns.Add(row); await db.SaveChangesAsync(ct);
        var visibility = new Mock<IAuthorVisibilityLookup>();
        Guid[] excluded = [];
        visibility.Setup(v => v.ExcludedUserIdsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => excluded);
        var clock = new Clock();
        using var cache = new CheckInPhotoVisibilityCache(clock, Options.Create(new CheckInPhotoDeliveryOptions { CachePublicVariants = true }));
        var queries = new CheckInPhotoAccessQueries(db, visibility.Object, cache);
        var first = await queries.GetVariantAsync(row.Id, photo.Id, other, ct);
        Assert.True(first!.IsPubliclyVisible);
        excluded = [owner]; clock.Advance(19);
        var hit = await queries.GetVariantAsync(row.Id, photo.Id, other, ct);
        Assert.Equal(first.PolicyObservation, hit!.PolicyObservation);
        Assert.Null(await queries.GetAsync(row.Id, photo.Id, other, ct)); // Original has no policy cache.
        clock.Advance(1);
        Assert.Null(await queries.GetVariantAsync(row.Id, photo.Id, other, ct));

        excluded = [];
        Assert.NotNull(await queries.GetVariantAsync(row.Id, photo.Id, owner, ct));
        row.ChangeVisibility(CheckInVisibility.Private); await db.SaveChangesAsync(ct);
        var privateAccess = await queries.GetVariantAsync(row.Id, photo.Id, owner, ct);
        Assert.False(privateAccess!.IsPubliclyVisible);
        excluded = [owner]; // Even an owner-only request observes a newly committed Account restriction.
        Assert.Null(await queries.GetVariantAsync(row.Id, photo.Id, owner, ct));
        Assert.Null(await queries.GetVariantAsync(row.Id, photo.Id, other, ct));
        row.Delete(); await db.SaveChangesAsync(ct);
        Assert.Null(await queries.GetVariantAsync(row.Id, photo.Id, owner, ct));
    }

    private sealed class Clock : TimeProvider
    {
        private long seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => seconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(seconds);
        public void Advance(int value) => seconds += value;
    }
}
