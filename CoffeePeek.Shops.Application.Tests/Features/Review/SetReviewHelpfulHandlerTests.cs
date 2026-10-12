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
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyVoteUsesCanonicalIdentityAndPublishesOnlyChangedState(bool changed)
    {
        var id = Guid.NewGuid();
        var voter = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var repository = new Mock<IQueryCheckInRepository>();
        var queries = new Mock<ICheckInQueries>();
        var bus = new Mock<IMessageBus>();
        queries.Setup(q => q.GetUnifiedByIdAsync(id, null, default))
            .ReturnsAsync(new UnifiedCheckInDto { Id = id, UserId = owner, ShopId = shopId });
        repository.Setup(r => r.SetHelpfulAsync(id, voter, true, default))
            .ReturnsAsync((true, 1, changed));

        var result = await CheckInHelpfulHandler.Handle(
            new SetCheckInHelpfulCommand(id, voter, true), repository.Object, queries.Object, bus.Object, default);

        Assert.True(result.Data.IsHelpful);
        Assert.Equal(1, result.Data.HelpfulCount);
        bus.Verify(b => b.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(message =>
            message.CheckInId == id && message.ShopId == shopId && message.UserId == owner)),
            changed ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task AuthorCannotVoteAndHiddenAliasDoesNotRevealContent()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var repository = new Mock<IQueryCheckInRepository>();
        var queries = new Mock<ICheckInQueries>();
        var bus = new Mock<IMessageBus>();
        queries.Setup(q => q.GetUnifiedByIdAsync(id, null, default))
            .ReturnsAsync(new UnifiedCheckInDto { Id = id, UserId = owner });
        await Assert.ThrowsAsync<ForbiddenException>(() => CheckInHelpfulHandler.Handle(
            new SetCheckInHelpfulCommand(id, owner, true), repository.Object, queries.Object, bus.Object, default));

        queries.Setup(q => q.GetUnifiedByIdAsync(id, null, default))
            .ReturnsAsync((UnifiedCheckInDto?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => CheckInHelpfulHandler.Handle(
            new SetCheckInHelpfulCommand(id, Guid.NewGuid(), true), repository.Object, queries.Object, bus.Object, default));
        repository.Verify(r => r.SetHelpfulAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never());
        bus.VerifyNoOtherCalls();
    }
}
