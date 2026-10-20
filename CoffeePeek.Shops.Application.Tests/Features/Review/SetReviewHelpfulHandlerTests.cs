using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Moq;
using Wolverine;

namespace CoffeePeek.Shops.Application.Tests.Features.Review.SetReviewHelpful;

public class SetReviewHelpfulHandlerTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task VoteUsesCanonicalIdentityAndPublishesOnlyChangedState(bool own, bool helpful, bool changed)
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var voter = own ? owner : Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var repository = new Mock<IQueryCheckInRepository>();
        var queries = new Mock<ICheckInQueries>();
        var bus = new Mock<IMessageBus>();
        queries.Setup(q => q.GetUnifiedByIdAsync(id, voter, default))
            .ReturnsAsync(new UnifiedCheckInDto { Id = id, UserId = owner, ShopId = shopId });
        repository.Setup(r => r.SetHelpfulAsync(id, voter, helpful, default))
            .ReturnsAsync((helpful, helpful ? 1 : 0, changed));

        var result = await CheckInHelpfulHandler.Handle(
            new SetCheckInHelpfulCommand(id, voter, helpful), repository.Object, queries.Object, bus.Object, default);

        Assert.Equal(helpful, result.Data.IsHelpful);
        Assert.Equal(helpful ? 1 : 0, result.Data.HelpfulCount);
        repository.Verify(r => r.SetHelpfulAsync(id, voter, helpful, default), Times.Once());
        bus.Verify(b => b.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(message =>
            message.CheckInId == id && message.ShopId == shopId && message.UserId == owner)),
            changed ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HiddenAliasDoesNotRevealContent(bool helpful)
    {
        var id = Guid.NewGuid();
        var repository = new Mock<IQueryCheckInRepository>();
        var queries = new Mock<ICheckInQueries>();
        var bus = new Mock<IMessageBus>();
        queries.Setup(q => q.GetUnifiedByIdAsync(id, It.IsAny<Guid?>(), default))
            .ReturnsAsync((UnifiedCheckInDto?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => CheckInHelpfulHandler.Handle(
            new SetCheckInHelpfulCommand(id, Guid.NewGuid(), helpful), repository.Object, queries.Object, bus.Object, default));
        repository.Verify(r => r.SetHelpfulAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never());
        bus.VerifyNoOtherCalls();
    }
}
