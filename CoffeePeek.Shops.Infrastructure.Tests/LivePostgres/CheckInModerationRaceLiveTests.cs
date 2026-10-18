using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wolverine;
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

    [Theory]
    [InlineData("hide")]
    [InlineData("delete")]
    [InlineData("edit")]
    [InlineData("photos")]
    public async Task AuthorChangeAfterConsumerRead_RejectsStaleApprovalAndKeepsEveryPublicProjectionClosed(string change)
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("mod-race-" + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("moderation-race-" + Guid.NewGuid().ToString("N"), city.Id);
        var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "author", "original",
            Rating.Create(4, 4, 4), DateTime.UtcNow.AddMinutes(-1), DomainVisibility.Public);
        await using (var seed = fixture.Context(false))
        {
            seed.Cities.Add(city);
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
                switch (change)
                {
                    case "hide": current.ChangeVisibility(DomainVisibility.Private); break;
                    case "delete": current.Delete(); break;
                    case "edit": current.UpdateContent("edited", Rating.Create(5, 5, 5)); break;
                    default:
                        current.AddPhotos([new CheckInPhoto("coffee.jpg", "image/jpeg", "check-ins/" + Guid.NewGuid(), 42, current.UserId)]);
                        break;
                }
                await hide.SaveChangesAsync(ct);
            }

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) => staleConsumer.SaveChangesAsync(token));
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                CheckInModerationDecidedHandler.Handle(decision, repository, unitOfWork.Object,
                    Mock.Of<IMessageBus>(), ct));
        }

        await using (var retry = fixture.Context(false))
        {
            var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
            await CheckInModerationDecidedHandler.Handle(decision, new QueryCheckInRepository(retry),
                unitOfWork.Object, Mock.Of<IMessageBus>(), ct);
            unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        await using var verify = fixture.Context(false);
        var persisted = await verify.CheckIns.AsNoTracking().SingleAsync(x => x.Id == checkIn.Id, ct);
        Assert.Equal(change == "hide" ? DomainVisibility.Private : DomainVisibility.Public, persisted.Visibility);
        Assert.Equal(2, persisted.ContentRevision);
        Assert.False(persisted.IsPubliclyVisible);
        var queries = new CheckInQueries(verify, Moq.Mock.Of<CoffeePeek.Shops.Application.Abstractions.IAuthorVisibilityLookup>());
        Assert.Null(await queries.GetUnifiedByIdAsync(checkIn.Id, null, ct));
        Assert.Null(await queries.GetModerationSnapshotAsync(checkIn.Id, 1, ct));
        Assert.Empty((await queries.GetPublicAsync(null, null, shop.Id, null, 10, null, ct)).Items);
        Assert.Equal(0, (await queries.GetStatsByShopAsync(shop.Id, ct)).Count);
    }
}
