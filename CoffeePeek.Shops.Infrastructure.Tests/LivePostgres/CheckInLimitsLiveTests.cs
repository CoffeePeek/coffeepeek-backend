using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CheckInLimitsLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;
    public CheckInLimitsLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCreatesSerializeCooldownAndDailyLimitIncludingDeletedVisits(bool dailyLimit)
    {
        var ct = TestContext.Current.CancellationToken;
        var user = Guid.NewGuid();
        var city = new City("limits-" + Guid.NewGuid().ToString("N"));
        var shops = Enumerable.Range(0, 7).Select(i => ShopsLiveDatabase.NewShop("limits-" + i, city.Id)).ToArray();
        var seeded = dailyLimit ? BusinessConstants.MaxUserCheckInsPerDay - 1 : 0;
        await using (var db = fixture.Context(false))
        {
            db.Cities.Add(city);
            db.Shops.AddRange(shops);
            for (var i = 0; i < seeded; i++)
            {
                var visit = CheckIn.CreateUnified(user, shops[i].Id, "author", "previous",
                    Rating.Create(4, 4, 4), DateTime.UtcNow);
                if (i == 0) visit.Delete();
                db.CheckIns.Add(visit);
            }
            await db.SaveChangesAsync(ct);
        }
        await using var blocker = await LivePostgresServer.OpenAsync(fixture.ConnectionString, ct);
        await using var blockerTx = await blocker.BeginTransactionAsync(ct);
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", blocker, blockerTx))
        {
            command.Parameters.AddWithValue("key", "checkin:" + user);
            await command.ExecuteNonQueryAsync(ct);
        }
        var first = CreateAsync(shops[5].Id);
        var second = CreateAsync(shops[dailyLimit ? 6 : 5].Id);
        await LivePostgresServer.WaitForLockWaitsAsync(fixture.ConnectionString, "advisory", 2, ct);
        await blockerTx.CommitAsync(ct);
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, success => success);
        await using var verify = fixture.Context(false);
        Assert.Equal(seeded + 1, await verify.CheckIns.CountAsync(c => c.UserId == user, ct));

        async Task<bool> CreateAsync(Guid shopId)
        {
            await using var db = fixture.Context(false);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var shopReader = new Mock<IQueryCoffeeShopRepository>();
            shopReader.Setup(s => s.Exists(shopId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            shopReader.Setup(s => s.GetShopNamesByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, string> { [shopId] = "shop" });
            var uow = new Mock<IUnitOfWork>();
            uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => db.SaveChangesAsync(token));
            try
            {
                var request = new CreateUnifiedCheckInRequest(shopId, VisitedAt: DateTimeOffset.UtcNow);
                var response = await CreateUnifiedCheckInHandler.Handle(new(request, user, "author"),
                    new QueryCheckInRepository(db), shopReader.Object, Mock.Of<IDrinkCatalog>(),
                    Mock.Of<IUploadedPhotoLookup>(), uow.Object, Mock.Of<IMessageBus>(), ct);
                Assert.True(response.IsSuccess);
                await tx.CommitAsync(ct);
                return true;
            }
            catch (ValidationException ex)
            {
                Assert.Contains(dailyLimit ? "limited" : "once every", ex.Message);
                await tx.RollbackAsync(ct);
                return false;
            }
        }
    }

    [Theory]
    [InlineData(CheckInVisibility.Private, true)]
    [InlineData(CheckInVisibility.Private, false)]
    [InlineData(CheckInVisibility.Public, true)]
    [InlineData(CheckInVisibility.Public, false)]
    public async Task OwnVotesRequirePublishedContent(CheckInVisibility visibility, bool helpful)
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("unpublished-votes-" + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("unpublished-votes", city.Id);
        var visit = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "author", "unpublished", Rating.Create(4, 4, 4),
            DateTime.UtcNow, visibility);
        await using var db = fixture.Context(false);
        db.Cities.Add(city); db.Shops.Add(shop); db.CheckIns.Add(visit);
        await db.SaveChangesAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new QueryCheckInRepository(db).SetHelpfulAsync(visit.Id, visit.UserId, helpful, ct));
        await tx.RollbackAsync(ct);
        Assert.Equal(0, await db.CheckInHelpfulVotes.CountAsync(v => v.CheckInId == visit.Id, ct));
        Assert.Equal(0, (await db.CheckIns.AsNoTracking().SingleAsync(v => v.Id == visit.Id, ct)).HelpfulCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task DuplicateConcurrentVotesChangeCounterExactlyOnce(bool helpful, bool own)
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("votes-" + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("votes", city.Id);
        var visit = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "author", "public", Rating.Create(4, 4, 4),
            DateTime.UtcNow, CheckInVisibility.Public);
        Assert.True(visit.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, DateTime.UtcNow));
        var voter = own ? visit.UserId : Guid.NewGuid();
        await using (var db = fixture.Context(false))
        {
            db.Cities.Add(city); db.Shops.Add(shop); db.CheckIns.Add(visit);
            await db.SaveChangesAsync(ct);
            if (!helpful)
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await new QueryCheckInRepository(db).SetHelpfulAsync(visit.Id, voter, true, ct);
                await tx.CommitAsync(ct);
            }
        }
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var changed = await Task.WhenAll(VoteAsync(), VoteAsync());
        Assert.Single(changed, c => c);
        await using var verify = fixture.Context(false);
        Assert.Equal(helpful ? 1 : 0, await verify.CheckInHelpfulVotes.CountAsync(v => v.CheckInId == visit.Id, ct));
        Assert.Equal(helpful ? 1 : 0, (await verify.CheckIns.SingleAsync(v => v.Id == visit.Id, ct)).HelpfulCount);

        async Task<bool> VoteAsync()
        {
            await using var db = fixture.Context(false);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (Interlocked.Increment(ref entered) == 2) bothEntered.TrySetResult();
            await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            var result = await new QueryCheckInRepository(db).SetHelpfulAsync(visit.Id, voter, helpful, ct);
            await tx.CommitAsync(ct);
            return result.Changed;
        }
    }
}
