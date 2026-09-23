using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Services;

public class CreateRoasterFromModerationServiceTests
{
    [Fact]
    public async Task CreateRoasterFromApprovedEventAsync_NewRoaster_InvalidatesAffectedCaches()
    {
        var queryRepository = new Mock<IQueryRoasterRepository>();
        var repository = new Mock<IRoasterRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var cacheService = new Mock<ICacheService>();
        var logger = new Mock<ILogger<CreateRoasterFromModerationService>>();
        var moderationId = Guid.NewGuid();
        var ct = CancellationToken.None;
        queryRepository
            .Setup(r => r.GetIdByModerationId(moderationId, ct))
            .ReturnsAsync((Guid?)null);

        var service = new CreateRoasterFromModerationService(
            queryRepository.Object,
            repository.Object,
            unitOfWork.Object,
            cacheService.Object,
            logger.Object);

        var result = await service.CreateRoasterFromApprovedEventAsync(
            new ModerationRoasterDto { Id = moderationId, Name = "Coffee Circus" },
            moderationId,
            ct);

        result.Should().NotBeEmpty();
        repository.Verify(r => r.Add(It.IsAny<Roaster>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
        cacheService.Verify(
            c => c.RemoveByPattern(CacheKey.Roaster.ListPattern(), ct),
            Times.Once);
        cacheService.Verify(
            c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct),
            Times.Once);
    }
}
