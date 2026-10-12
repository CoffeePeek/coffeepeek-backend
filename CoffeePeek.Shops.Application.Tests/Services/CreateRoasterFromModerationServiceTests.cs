using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Services;

public class CreateRoasterFromModerationServiceTests
{
    [Fact]
    public async Task CreateRoasterFromApprovedEventAsync_NewRoaster_IsTrackedForConsumerCommit()
    {
        var queryRepository = new Mock<IQueryRoasterRepository>();
        var repository = new Mock<IRoasterRepository>();
        var logger = new Mock<ILogger<CreateRoasterFromModerationService>>();
        var moderationId = Guid.NewGuid();
        var ct = CancellationToken.None;
        queryRepository
            .Setup(r => r.GetIdByModerationId(moderationId, ct))
            .ReturnsAsync((Guid?)null);

        var service = new CreateRoasterFromModerationService(
            queryRepository.Object,
            repository.Object,
            logger.Object);

        var result = await service.CreateRoasterFromApprovedEventAsync(
            new ModerationRoasterDto { Id = moderationId, Name = "Coffee Circus" },
            moderationId,
            ct);

        result.Should().NotBeEmpty();
        repository.Verify(r => r.Add(It.Is<Roaster>(roaster => roaster.Id == result && roaster.ModerationId == moderationId)), Times.Once);
    }

    [Fact]
    public async Task CreateRoasterFromApprovedEventAsync_ReplayedEvent_ReturnsExistingRoaster()
    {
        var queryRepository = new Mock<IQueryRoasterRepository>();
        var repository = new Mock<IRoasterRepository>();
        var existingId = Guid.NewGuid();
        var moderationId = Guid.NewGuid();
        queryRepository.Setup(r => r.GetIdByModerationId(moderationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId);

        var service = new CreateRoasterFromModerationService(
            queryRepository.Object,
            repository.Object,
            Mock.Of<ILogger<CreateRoasterFromModerationService>>());

        var result = await service.CreateRoasterFromApprovedEventAsync(
            new ModerationRoasterDto { Id = moderationId, Name = "Coffee Circus" },
            moderationId);

        result.Should().Be(existingId);
        repository.Verify(r => r.Add(It.IsAny<Roaster>()), Times.Never);
    }
}
