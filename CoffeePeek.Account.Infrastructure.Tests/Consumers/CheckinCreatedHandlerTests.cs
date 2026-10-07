using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Infrastructure.Consumers;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Account.Infrastructure.Tests.Consumers;

public class CheckinCreatedHandlerTests
{
    [Fact]
    public async Task Handle_CanonicalCreationIncrementsOnlyAuthorCheckInCountAndSaves()
    {
        var repository = new Mock<IUserRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var user = User.Register("user@example.com", "testuser", "hash", Role.Create("User"));
        var message = new CheckinCreatedEvent
        {
            UserId = user.Id,
            ShopId = Guid.NewGuid(),
            CheckInId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        var ct = TestContext.Current.CancellationToken;
        repository.Setup(r => r.GetById(user.Id, ct)).ReturnsAsync(user);
        unitOfWork.Setup(u => u.SaveChangesAsync(ct)).ReturnsAsync(1);

        await new CheckinCreatedHandler(repository.Object, unitOfWork.Object).Handle(message, ct);

        user.Statistics.CheckInCount.Should().Be(1);
        user.Statistics.AddedShopsCount.Should().Be(0);
        repository.Verify(r => r.GetById(user.Id, ct), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingAuthorFailsBeforeSaving()
    {
        var repository = new Mock<IUserRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var message = new CheckinCreatedEvent { UserId = Guid.NewGuid(), CheckInId = Guid.NewGuid() };

        var act = () => new CheckinCreatedHandler(repository.Object, unitOfWork.Object)
            .Handle(message, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
