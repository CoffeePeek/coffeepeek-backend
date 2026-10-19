using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Infrastructure.Account;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CheckInPhotoVariantPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshOwnerOnlyVariantAccessMakesOneAccountLookup(bool pendingPublic)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context();
        var (row, photo) = await OwnerOnlyPhotoAsync(db, pendingPublic, ct);
        var visibility = new Mock<IAuthorVisibilityLookup>();
        visibility.Setup(v => v.ExcludedUserIdsAsync(row.UserId, ct)).ReturnsAsync([]);
        using var cache = new CheckInPhotoVisibilityCache(new Clock());
        var queries = new CheckInPhotoAccessQueries(db, visibility.Object, cache);

        var result = await queries.GetVariantAsync(row.Id, photo.Id, row.UserId, ct);

        Assert.NotNull(result);
        Assert.False(result.IsPubliclyVisible);
        visibility.Verify(v => v.ExcludedUserIdsAsync(row.UserId, ct), Times.Once);
    }

    [Fact]
    public async Task CachedPrivateFactsRecheckAccountAndRejectANewBlockEvenAtTheSameTimestamp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context();
        var (row, photo) = await OwnerOnlyPhotoAsync(db, false, ct);
        var visibility = new Mock<IAuthorVisibilityLookup>();
        Guid[] excluded = [];
        visibility.Setup(v => v.ExcludedUserIdsAsync(row.UserId, ct)).ReturnsAsync(() => excluded);
        using var cache = new CheckInPhotoVisibilityCache(new Clock());
        var queries = new CheckInPhotoAccessQueries(db, visibility.Object, cache);
        Assert.NotNull(await queries.GetVariantAsync(row.Id, photo.Id, row.UserId, ct));
        visibility.Verify(v => v.ExcludedUserIdsAsync(row.UserId, ct), Times.Once);

        excluded = [row.UserId];
        Assert.Null(await queries.GetVariantAsync(row.Id, photo.Id, row.UserId, ct));
        visibility.Verify(v => v.ExcludedUserIdsAsync(row.UserId, ct), Times.Exactly(2));
    }

    [Fact]
    public async Task CachedPrivateFactsFailClosedWhenTheFreshAccountRecheckFails()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context();
        var (row, photo) = await OwnerOnlyPhotoAsync(db, false, ct);
        var visibility = new Mock<IAuthorVisibilityLookup>();
        visibility.Setup(v => v.ExcludedUserIdsAsync(row.UserId, ct)).ReturnsAsync([]);
        using var cache = new CheckInPhotoVisibilityCache(new Clock());
        var queries = new CheckInPhotoAccessQueries(db, visibility.Object, cache);
        Assert.NotNull(await queries.GetVariantAsync(row.Id, photo.Id, row.UserId, ct));
        visibility.Setup(v => v.ExcludedUserIdsAsync(row.UserId, ct))
            .ThrowsAsync(new ServiceUnavailableException("Account unavailable"));

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => queries.GetVariantAsync(row.Id, photo.Id, row.UserId, ct));
        visibility.Verify(v => v.ExcludedUserIdsAsync(row.UserId, ct), Times.Exactly(2));
    }

    [Fact]
    public async Task UnknownFactsProvenanceCannotBypassTheFreshPrivateAccountRecheck()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Context();
        var (row, photo) = await OwnerOnlyPhotoAsync(db, false, ct);
        var visibility = new Mock<IAuthorVisibilityLookup>();
        visibility.Setup(v => v.ExcludedUserIdsAsync(row.UserId, ct)).ReturnsAsync([row.UserId]);
        var facts = new Mock<ICheckInPhotoVisibilityCache>();
        facts.Setup(f => f.GetAsync(row.UserId, visibility.Object, ct))
            .ReturnsAsync(new CheckInPhotoVisibilityFacts([], CheckInPhotoPolicyObservation.Start(new Clock())));

        Assert.Null(await new CheckInPhotoAccessQueries(db, visibility.Object, facts.Object)
            .GetVariantAsync(row.Id, photo.Id, row.UserId, ct));
        visibility.Verify(v => v.ExcludedUserIdsAsync(row.UserId, ct), Times.Once);
    }

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
        using var cache = new CheckInPhotoVisibilityCache(clock);
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

    private static ShopsDbContext Context() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(CheckIn Row, CheckInPhoto Photo)> OwnerOnlyPhotoAsync(ShopsDbContext db, bool pendingPublic,
        CancellationToken ct)
    {
        var owner = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var row = CheckIn.CreateHome(owner, "author", null, now, DateOnly.FromDateTime(now), null, null, null, null, null, null);
        var photo = new CheckInPhoto("photo.jpg", "image/jpeg", "private/photo", 100, owner);
        row.AddPhotos([photo]);
        if (pendingPublic) row.ChangeVisibility(CheckInVisibility.Public);
        db.CheckIns.Add(row);
        await db.SaveChangesAsync(ct);
        return (row, photo);
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
