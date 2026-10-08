using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;
using DomainVisibility = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckInVisibility;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CheckInModerationRaceLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public CheckInModerationRaceLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task HideAfterConsumerRead_RejectsStaleApprovalAndKeepsEveryPublicProjectionClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = ShopsLiveDatabase.NewShop("moderation-race-" + Guid.NewGuid().ToString("N"));
        var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "author", "original",
            Rating.Create(4, 4, 4), DateTime.UtcNow.AddMinutes(-1), DomainVisibility.Public);
        await using (var seed = fixture.Context(false))
        {
            seed.Shops.Add(shop);
            seed.CheckIns.Add(checkIn);
            await seed.SaveChangesAsync(ct);
        }

        var decision = new CheckInModerationDecidedEvent(checkIn.Id, 1, Guid.NewGuid(), 1,
            ModerationStatus.Approved, null, DateTime.UtcNow);
        await using (var staleConsumer = fixture.Context(false))
        {
            var repository = new QueryCheckInRepository(staleConsumer);
            Assert.NotNull(await repository.GetTrackedByIdAsync(checkIn.Id, ct));

            await using (var hide = fixture.Context(false))
            {
                var current = await hide.CheckIns.SingleAsync(x => x.Id == checkIn.Id, ct);
                current.ChangeVisibility(DomainVisibility.Private);
                await hide.SaveChangesAsync(ct);
            }

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => staleConsumer.SaveChangesAsync(token));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                CheckInModerationDecidedHandler.Handle(decision, repository, unitOfWork.Object,
                    Mock.Of<ICacheService>(), ct));
        }

        await using (var retry = fixture.Context(false))
        {
            var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
            await CheckInModerationDecidedHandler.Handle(decision, new QueryCheckInRepository(retry),
                unitOfWork.Object, Mock.Of<ICacheService>(), ct);
            unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        await using var verify = fixture.Context(false);
        var persisted = await verify.CheckIns.AsNoTracking().SingleAsync(x => x.Id == checkIn.Id, ct);
        Assert.Equal(DomainVisibility.Private, persisted.Visibility);
        Assert.Equal(2, persisted.ContentRevision);
        Assert.False(persisted.IsPubliclyVisible);
        var queries = new CheckInQueries(verify);
        Assert.Null(await queries.GetUnifiedByIdAsync(checkIn.Id, null, ct));
        Assert.Null(await queries.GetModerationSnapshotAsync(checkIn.Id, 1, ct));
        Assert.Empty((await queries.GetPublicAsync(null, null, shop.Id, null, 10, null, ct)).Items);
        Assert.Equal(0, (await queries.GetStatsByShopAsync(shop.Id, ct)).Count);
    }
}
