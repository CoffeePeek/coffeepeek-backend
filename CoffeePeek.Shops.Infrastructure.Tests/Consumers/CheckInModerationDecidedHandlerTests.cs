using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Infrastructure.Consumers;
using Moq;
using Wolverine;
using State = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckInModerationState;
using Visibility = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckInVisibility;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public class CheckInModerationDecidedHandlerTests
{
    [Fact]
    public async Task DecisionsAreCurrentOrderedAndFirstPublicationIsIdempotent()
    {
        var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", "x",
            Rating.Create(1, 2, 3), DateTime.UtcNow, Visibility.Public);
        var repository = new Mock<IQueryCheckInRepository>();
        repository.Setup(r => r.GetTrackedByIdAsync(checkIn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIn);
        var unitOfWork = new Mock<IUnitOfWork>();
        var bus = new Mock<IMessageBus>();
        var decision = new CheckInModerationDecidedEvent(checkIn.Id, 1, Guid.NewGuid(), 1,
            ModerationStatus.Approved, null, DateTime.UtcNow);

        await CheckInModerationDecidedHandler.Handle(decision, repository.Object, unitOfWork.Object, bus.Object, default);
        Assert.True(checkIn.IsPubliclyVisible);
        var version = checkIn.RowVersion;
        await CheckInModerationDecidedHandler.Handle(decision, repository.Object, unitOfWork.Object, bus.Object, default);
        Assert.Equal(version, checkIn.RowVersion);
        await CheckInModerationDecidedHandler.Handle(
            decision with { DecisionSequence = 2, Status = ModerationStatus.Pending },
            repository.Object, unitOfWork.Object, bus.Object, default);
        Assert.False(checkIn.IsPubliclyVisible);
        await CheckInModerationDecidedHandler.Handle(decision, repository.Object, unitOfWork.Object, bus.Object, default);
        await CheckInModerationDecidedHandler.Handle(
            decision with { DecisionSequence = 3 }, repository.Object, unitOfWork.Object, bus.Object, default);
        Assert.True(checkIn.IsPubliclyVisible);
        checkIn.UpdateContent("new", Rating.Create(5, 5, 5));
        await CheckInModerationDecidedHandler.Handle(
            decision with { DecisionSequence = 99 }, repository.Object, unitOfWork.Object, bus.Object, default);
        Assert.Equal(State.Pending, checkIn.ModerationState);
        checkIn.ChangeVisibility(Visibility.Private);
        await CheckInModerationDecidedHandler.Handle(
            decision with { ContentRevision = checkIn.ContentRevision, DecisionSequence = 100 },
            repository.Object, unitOfWork.Object, bus.Object, default);
        Assert.False(checkIn.IsPubliclyVisible);
        checkIn.Delete();
        await CheckInModerationDecidedHandler.Handle(
            decision with { ContentRevision = checkIn.ContentRevision, DecisionSequence = 101 },
            repository.Object, unitOfWork.Object, bus.Object, default);
        Assert.False(checkIn.IsPubliclyVisible);

        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
        bus.Verify(b => b.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(message =>
            message.CheckInId == checkIn.Id && message.ShopId == checkIn.ShopId && message.UserId == checkIn.UserId)),
            Times.Exactly(3));
    }
}
