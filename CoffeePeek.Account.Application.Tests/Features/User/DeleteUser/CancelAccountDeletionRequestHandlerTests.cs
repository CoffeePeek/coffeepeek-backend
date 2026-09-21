using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.User.DeleteUser;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.User.DeleteUser;

public class CancelAccountDeletionRequestHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_WithValidPendingToken_CancelsRequest()
    {
        var repository = new Mock<IAccountDeletionRequestRepository>();
        var tokenService = new Mock<IAccountDeletionTokenService>();
        var timeProvider = new Mock<TimeProvider>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var request = CreateRequest();
        tokenService.Setup(x => x.Hash("token")).Returns("hash");
        repository.Setup(x => x.GetPendingByTokenHash("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        timeProvider.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(Now.AddMinutes(1)));

        var response = await CancelAccountDeletionRequestHandler.Handle(
            new CancelAccountDeletionRequestCommand("token"),
            repository.Object,
            tokenService.Object,
            timeProvider.Object,
            unitOfWork.Object,
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(AccountDeletionRequestStatus.Cancelled);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithUnknownToken_ReturnsNotFound()
    {
        var repository = new Mock<IAccountDeletionRequestRepository>();
        var tokenService = new Mock<IAccountDeletionTokenService>();
        tokenService.Setup(x => x.Hash("token")).Returns("hash");

        var response = await CancelAccountDeletionRequestHandler.Handle(
            new CancelAccountDeletionRequestCommand("token"),
            repository.Object,
            tokenService.Object,
            Mock.Of<TimeProvider>(),
            Mock.Of<IUnitOfWork>(),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(404);
    }

    private static AccountDeletionRequest CreateRequest() =>
        AccountDeletionRequest.Create(
            Guid.NewGuid(),
            AccountDeletionReason.PrivacyConcerns,
            null,
            "hash",
            Now,
            TimeSpan.FromHours(24),
            TimeSpan.FromMinutes(5));
}
