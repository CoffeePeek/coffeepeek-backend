using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class CheckInPhotoAccessQueryTests
{
    [Fact]
    public void PostgresSqlSelectsOnePhotoWithoutLoadingVotesNotesOrTheFullGallery()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=test").Options);
        var queries = new CheckInPhotoAccessQueries(db, Mock.Of<IAuthorVisibilityLookup>());
        var sql = queries.ReaderQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]).ToQueryString();
        Assert.Contains("JOIN", sql);
        Assert.Contains("CheckInPhotos", sql);
        Assert.Contains("ApprovedRevision", sql);
        Assert.Contains("IsHidden", sql);
        Assert.DoesNotContain("CheckInHelpfulVotes", sql);
        Assert.Contains("\"StorageKey\"", sql); // Bind variant version to the current source.
        Assert.DoesNotContain("\"Note\"", sql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SinglePhotoAccessMatchesDetailsAcrossVisibilityModerationAndDeletion(bool home)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var shop = new CoffeeShop(null, "photo access", null, PriceRange.Moderate, Guid.NewGuid());
        db.Shops.Add(shop);
        var time = DateTime.UtcNow.AddDays(-1);
        var owner = Guid.NewGuid();
        var row = home
            ? CheckIn.CreateHome(owner, "owner", null, time, DateOnly.FromDateTime(time), null, null, null, null, null, null)
            : CheckIn.CreateUnified(owner, shop.Id, "owner", null, null, time);
        var gallery = new CheckInPhoto("photo.jpg", "image/jpeg", "private/gallery", 10, owner);
        var cup = new CheckInPhoto("cup.png", "image/png", "private/cup", 20, owner);
        row.AddPhotos([gallery]); row.AddDrinkPhoto(cup); db.CheckIns.Add(row);
        var visibility = new Mock<IAuthorVisibilityLookup>();
        var excluded = Array.Empty<Guid>();
        visibility.Setup(v => v.ExcludedUserIdsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => excluded);
        var original = new CheckInQueries(db, visibility.Object);
        var narrow = new CheckInPhotoAccessQueries(db, visibility.Object);
        var other = Guid.NewGuid();

        async Task AssertParity()
        {
            await db.SaveChangesAsync(ct);
            foreach (var reader in new Guid?[] { null, owner, other })
            {
                var detail = await original.GetUnifiedByIdAsync(row.Id, reader, ct);
                foreach (var photo in new[] { gallery, cup })
                {
                    var access = await narrow.GetAsync(row.Id, photo.Id, reader, ct);
                    Assert.Equal(detail is not null, access is not null);
                    if (access is not null)
                    {
                        Assert.Equal(owner, access.OwnerId);
                        Assert.Equal(photo.ContentType, access.ContentType);
                        Assert.Equal(row.ContentRevision, access.ContentRevision);
                        Assert.Equal(home ? Contract.Enums.CheckInKind.Home : Contract.Enums.CheckInKind.Shop, access.Kind);
                    }
                }
                Assert.Null(await narrow.GetAsync(row.Id, Guid.NewGuid(), reader, ct));
            }
            foreach (var revision in new[] { row.ContentRevision, row.ContentRevision - 1 })
            {
                var snapshot = await original.GetModerationSnapshotAsync(row.Id, revision, ct);
                Assert.Equal(snapshot is not null, await narrow.GetModerationAsync(row.Id, cup.Id, revision, ct) is not null);
            }
        }

        await AssertParity(); // Private, owner only.
        row.ChangeVisibility(CheckInVisibility.Public); await AssertParity(); // Pending.
        var submission = Guid.NewGuid();
        Assert.True(row.ApplyModerationDecision(row.ContentRevision, submission, 1, CheckInModerationState.Rejected, "test", DateTime.UtcNow));
        await AssertParity();
        Assert.True(row.ApplyModerationDecision(row.ContentRevision, submission, 2, CheckInModerationState.Approved, null, DateTime.UtcNow));
        await AssertParity();
        Assert.NotNull(await narrow.GetAsync(row.Id, gallery.Id, null, ct));
        excluded = [owner]; await AssertParity(); // Account returns both directions of personal blocking.
        Assert.Null(await narrow.GetAsync(row.Id, gallery.Id, other, ct));
        excluded = []; shop.SetHidden(true); await AssertParity();
        row.ChangeVisibility(CheckInVisibility.Private); await AssertParity();
        row.Delete(); await AssertParity();
        Assert.Null(await narrow.GetAsync(row.Id, gallery.Id, owner, ct));
    }

    [Fact]
    public async Task AccountFailureDoesNotFallBackToOwnerOrPublicAccess()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var visibility = new Mock<IAuthorVisibilityLookup>();
        visibility.Setup(v => v.ExcludedUserIdsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CoffeePeek.Shared.Kernel.Exceptions.ServiceUnavailableException("unavailable"));
        await Assert.ThrowsAsync<CoffeePeek.Shared.Kernel.Exceptions.ServiceUnavailableException>(() =>
            new CheckInPhotoAccessQueries(db, visibility.Object).GetAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                TestContext.Current.CancellationToken));
    }
}
