using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Application.Features.User.DeleteUser;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.User.DeleteUser;

public class DeleteUserHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IAccountDeletionRequestRepository> _deletionRepoMock = new();
    private readonly Mock<IAccountDeletionTokenService> _tokenServiceMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly IOptions<AccountDeletionOptions> _options = Options.Create(new AccountDeletionOptions
    {
        TokenPepper = "test-pepper-at-least-thirty-two-characters",
        TokenLifetimeHours = 24,
        ResendCooldownMinutes = 5,
        MaxEmailsPerHour = 3
    });

    public DeleteUserHandlerTests()
    {
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(Now));
        _tokenServiceMock.Setup(x => x.Generate()).Returns(("plain-token", new string('a', 64)));
    }

    private static DomainUser CreateUser()
    {
        var role = Role.Create("User");
        return DomainUser.Register("user@example.com", "testuser", "hash", role);
    }

    [Fact]
    public async Task Handle_WhenNoActiveRequest_CreatesPendingRequestAndEmailEvent()
    {
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _deletionRepoMock.Setup(r => r.GetActiveByUserId(user.Id, _ct))
            .ReturnsAsync((AccountDeletionRequest?)null);

        var (response, @event) = await DeleteUserHandler.Handle(
            new DeleteUserCommand(user.Id),
            _userRepoMock.Object,
            _deletionRepoMock.Object,
            _tokenServiceMock.Object,
            _options,
            _timeProviderMock.Object,
            _unitOfWorkMock.Object,
            _ct);

        response.IsSuccess.Should().BeTrue();
        response.Data.Status.Should().Be(AccountDeletionRequestStatus.PendingConfirmation);
        response.Data.ExpiresAtUtc.Should().Be(Now.AddHours(24));
        @event.Should().NotBeNull();
        @event!.ConfirmationToken.Should().Be("plain-token");
        _deletionRepoMock.Verify(r => r.Add(It.IsAny<AccountDeletionRequest>(), _ct), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldKeepUserActiveAndSessionsUntouched()
    {
        var user = CreateUser();
        user.AddSession("refresh-token", TimeSpan.FromDays(7), "device", "127.0.0.1");
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);

        await DeleteUserHandler.Handle(
            new DeleteUserCommand(user.Id),
            _userRepoMock.Object,
            _deletionRepoMock.Object,
            _tokenServiceMock.Object,
            _options,
            _timeProviderMock.Object,
            _unitOfWorkMock.Object,
            _ct);

        user.IsSoftDelete.Should().BeFalse();
        user.RefreshTokens.Should().OnlyContain(t => t.IsActive);
    }

    [Fact]
    public async Task Handle_WhenPendingRequestIsInCooldown_ReturnsExistingRequestWithoutEmail()
    {
        var user = CreateUser();
        var request = CreateRequest(user.Id);
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _deletionRepoMock.Setup(r => r.GetActiveByUserId(user.Id, _ct)).ReturnsAsync(request);

        var (response, @event) = await DeleteUserHandler.Handle(
            new DeleteUserCommand(user.Id),
            _userRepoMock.Object,
            _deletionRepoMock.Object,
            _tokenServiceMock.Object,
            _options,
            _timeProviderMock.Object,
            _unitOfWorkMock.Object,
            _ct);

        response.Data.RequestId.Should().Be(request.Id);
        @event.Should().BeNull();
        _tokenServiceMock.Verify(x => x.Generate(), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCooldownElapsed_RotatesTokenAndSendsEmail()
    {
        var user = CreateUser();
        var request = CreateRequest(user.Id);
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(Now.AddMinutes(5)));
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _deletionRepoMock.Setup(r => r.GetActiveByUserId(user.Id, _ct)).ReturnsAsync(request);

        var (_, @event) = await DeleteUserHandler.Handle(
            new DeleteUserCommand(user.Id),
            _userRepoMock.Object,
            _deletionRepoMock.Object,
            _tokenServiceMock.Object,
            _options,
            _timeProviderMock.Object,
            _unitOfWorkMock.Object,
            _ct);

        request.LastResentAtUtc.Should().Be(Now.AddMinutes(5));
        @event.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundWithoutPersisting()
    {
        _userRepoMock.Setup(r => r.GetById(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainUser?)null);

        var (response, @event) = await DeleteUserHandler.Handle(
            new DeleteUserCommand(Guid.NewGuid()),
            _userRepoMock.Object,
            _deletionRepoMock.Object,
            _tokenServiceMock.Object,
            _options,
            _timeProviderMock.Object,
            _unitOfWorkMock.Object,
            _ct);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(404);
        @event.Should().BeNull();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AccountDeletionRequest CreateRequest(Guid userId) =>
        AccountDeletionRequest.Create(
            userId,
            new string('b', 64),
            Now,
            TimeSpan.FromHours(24),
            TimeSpan.FromMinutes(5));
}
