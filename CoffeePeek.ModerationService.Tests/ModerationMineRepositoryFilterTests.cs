using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeeShop.Moderation.Persistence.Configuration;
using CoffeeShop.Moderation.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ModerationReview = CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate.ModerationReview;

namespace CoffeePeek.ModerationService.Tests;

/// <summary>
/// Guards the submitter filter behind GET */mine: a user must only ever see their own submissions,
/// and a null filter must keep the moderator view (everything) unchanged.
/// </summary>
public class ModerationMineRepositoryFilterTests
{
    private const string RejectReason = "Photos do not show the coffee bar";

    private static DbContextOptions<ModerationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<ModerationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static async Task Seed(DbContextOptions<ModerationDbContext> options, Action<ModerationDbContext> add)
    {
        await using var seed = new ModerationDbContext(options);
        add(seed);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Roasters_FilteredByUser_ReturnOnlyThatUsersItems()
    {
        var options = NewOptions();
        var userA = Guid.NewGuid();
        var a1 = ModerationRoaster.Create("A one", userA, null);
        var a2 = ModerationRoaster.Create("A two", userA, null);
        await Seed(options, db => db.ModerationRoasters.AddRange(
            a1, a2,
            ModerationRoaster.Create("B one", Guid.NewGuid(), null),
            ModerationRoaster.Create("Anonymous", null, null)));

        await using var db = new ModerationDbContext(options);
        var (items, total) = await new QueryModerationRoasterRepository(db).GetPagedForReviewAsync(
            1, 20, null, userId: userA, ct: TestContext.Current.CancellationToken);

        total.Should().Be(2);
        items.Select(r => r.Id).Should().BeEquivalentTo([a1.Id, a2.Id]);
    }

    [Fact]
    public async Task Roasters_FilteredByUserAndStatus_ReturnOnlyMatchingItem()
    {
        var options = NewOptions();
        var userA = Guid.NewGuid();
        var rejected = ModerationRoaster.Create("A rejected", userA, null);
        rejected.Reject(RejectReason);
        await Seed(options, db => db.ModerationRoasters.AddRange(
            rejected,
            ModerationRoaster.Create("A pending", userA, null),
            ModerationRoaster.Create("B pending", Guid.NewGuid(), null)));

        await using var db = new ModerationDbContext(options);
        var (items, total) = await new QueryModerationRoasterRepository(db).GetPagedForReviewAsync(
            1, 20, ModerationStatus.Rejected, userId: userA, ct: TestContext.Current.CancellationToken);

        total.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(rejected.Id);
        items[0].RejectedReason.Should().Be(RejectReason);
    }

    [Fact]
    public async Task Roasters_WithoutUserFilter_ReturnEverything()
    {
        var options = NewOptions();
        var userA = Guid.NewGuid();
        await Seed(options, db => db.ModerationRoasters.AddRange(
            ModerationRoaster.Create("A one", userA, null),
            ModerationRoaster.Create("A two", userA, null),
            ModerationRoaster.Create("B one", Guid.NewGuid(), null),
            ModerationRoaster.Create("Anonymous", null, null)));

        await using var db = new ModerationDbContext(options);
        var (items, total) = await new QueryModerationRoasterRepository(db).GetPagedForReviewAsync(
            1, 20, null, ct: TestContext.Current.CancellationToken);

        total.Should().Be(4);
        items.Should().HaveCount(4);
    }

    [Fact]
    public async Task Reviews_FilteredByUser_ReturnOnlyThatUsersItems()
    {
        var options = NewOptions();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var a1 = NewReview(userA);
        var a2 = NewReview(userA);
        await Seed(options, db => db.ModerationReviews.AddRange(a1, a2, NewReview(userB)));

        await using var db = new ModerationDbContext(options);
        var (items, total) = await new QueryModerationReviewRepository(db).GetPagedAsync(
            1, 20, null, null, userId: userA, ct: TestContext.Current.CancellationToken);

        total.Should().Be(2);
        items.Select(r => r.Id).Should().BeEquivalentTo([a1.Id, a2.Id]);
    }

    [Fact]
    public async Task Shops_FilteredByUser_ReturnOnlyThatUsersItems()
    {
        var options = NewOptions();
        var userA = Guid.NewGuid();
        var a1 = ModerationShop.Create("A cafe", userA, Guid.NewGuid(), null);
        await Seed(options, db => db.ModerationShops.AddRange(
            a1,
            ModerationShop.Create("B cafe", Guid.NewGuid(), Guid.NewGuid(), null),
            ModerationShop.Create("Anonymous cafe", null, Guid.NewGuid(), null)));

        await using var db = new ModerationDbContext(options);
        var (items, total) = await new QueryModerationShopRepository(db).GetPagedForReviewAsync(
            1, 20, null, null, null, userId: userA, ct: TestContext.Current.CancellationToken);

        total.Should().Be(1);
        items.Should().ContainSingle().Which.Id.Should().Be(a1.Id);
    }

    private static ModerationReview NewReview(Guid userId) =>
        ModerationReview.Create(
            userId, Guid.NewGuid(), null, "user", "Nice header", "A long enough review comment",
            4, 4, 4, new List<PhotoMetadata>());
}
