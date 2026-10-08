using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Infrastructure.Consumers;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace CoffeePeek.Account.Infrastructure.Tests.Consumers;

public class ModerationShopApprovedAccountHandlerTests
{
    [Fact]
    public async Task Handle_PassesCancellationTokenToGetByIdAndSaveChanges()
    {
        var userRepo = new Mock<IUserRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var logger = new Mock<ILogger<ModerationShopApprovedAccountHandler>>();
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        var user = User.Register("user@example.com", "testuser", "hash", Role.Create("User"));
        userRepo.Setup(r => r.GetById(user.Id, ct)).ReturnsAsync(user);
        unitOfWork.Setup(u => u.SaveChangesAsync(ct)).ReturnsAsync(1);

        var sut = new ModerationShopApprovedAccountHandler(userRepo.Object, unitOfWork.Object, logger.Object);
        var message = new ModerationShopApprovedEvent(user.Id, new ShopDto
        {
            Name = "Cafe",
            Photos = [],
            CheckIns = []
        });

        await sut.Handle(message, ct);

        user.Statistics.AddedShopsCount.Should().Be(1);
        userRepo.Verify(r => r.GetById(user.Id, ct), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
    }
}
