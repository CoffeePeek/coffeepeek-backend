using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Infrastructure.Consumers;
using Moq;
using State = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckInModerationState;
using Visibility = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckInVisibility;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public class CheckInModerationDecidedHandlerTests
{
    [Fact]
    public async Task DecisionsAreCurrentOrderedAndFirstPublicationIsIdempotent()
    {
        var c = CheckIn.CreateUnified(Guid.NewGuid(),Guid.NewGuid(),"author","x",Rating.Create(1,2,3),DateTime.UtcNow,Visibility.Public);
        var repo = new Mock<IQueryCheckInRepository>();
        repo.Setup(r=>r.GetTrackedByIdAsync(c.Id,It.IsAny<CancellationToken>())).ReturnsAsync(c);
        var uow = new Mock<IUnitOfWork>();
        var cache = new Mock<ICacheService>();
        var event1 = new CheckInModerationDecidedEvent(c.Id,1,Guid.NewGuid(),1,ModerationStatus.Approved,null,DateTime.UtcNow);
        await CheckInModerationDecidedHandler.Handle(event1,repo.Object,uow.Object,cache.Object,default);
        Assert.True(c.IsPubliclyVisible);
        var version = c.RowVersion;
        await CheckInModerationDecidedHandler.Handle(event1,repo.Object,uow.Object,cache.Object,default);
        Assert.Equal(version,c.RowVersion);
        await CheckInModerationDecidedHandler.Handle(event1 with { DecisionSequence=2, Status=ModerationStatus.Pending },repo.Object,uow.Object,cache.Object,default);
        Assert.False(c.IsPubliclyVisible);
        await CheckInModerationDecidedHandler.Handle(event1,repo.Object,uow.Object,cache.Object,default);
        await CheckInModerationDecidedHandler.Handle(event1 with { DecisionSequence=3 },repo.Object,uow.Object,cache.Object,default);
        Assert.True(c.IsPubliclyVisible);
        c.UpdateContent("new",Rating.Create(5,5,5));
        await CheckInModerationDecidedHandler.Handle(event1 with { DecisionSequence=99 },repo.Object,uow.Object,cache.Object,default);
        Assert.Equal(State.Pending,c.ModerationState);
        c.ChangeVisibility(Visibility.Private);
        await CheckInModerationDecidedHandler.Handle(event1 with { ContentRevision=c.ContentRevision,DecisionSequence=100 },repo.Object,uow.Object,cache.Object,default);
        Assert.False(c.IsPubliclyVisible);
        c.Delete();
        await CheckInModerationDecidedHandler.Handle(event1 with { ContentRevision=c.ContentRevision,DecisionSequence=101 },repo.Object,uow.Object,cache.Object,default);
        Assert.False(c.IsPubliclyVisible);
    }
}
