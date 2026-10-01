using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.User.DeleteUser;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Contract.Events.Account;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.User.DeleteUser;

public class ConfirmAccountDeletionHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);
    private readonly Mock<IAccountDeletionRequestRepository> _deletionRepository = new();
    private readonly Mock<IAccountDeletionTokenService> _tokenService = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IRevokedUserStore> _revokedUserStore = new();
    private readonly Mock<ISessionTerminationNotifier> _notifier = new();
    private readonly Mock<TimeProvider> _timeProvider = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly IOptions<JWTOptions> _jwtOptions = Options.Create(new JWTOptions
    {
        SecretKey = "test-secret-at-least-thirty-two-characters",
        Issuer = "issuer",
        Audience = "audience",
        AccessTokenLifetimeMinutes = 60,
        RefreshTokenLifetimeDays = 7
    });

    public ConfirmAccountDeletionHandlerTests()
    {
        _tokenService.Setup(x => x.Hash("raw-token")).Returns("hash");
        _timeProvider.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(Now));
        _revokedUserStore
            .Setup(x => x.RevokeAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task Handle_WithPendingRequest_DeletesUserRevokesSessionsAndEmitsEvent()
    {
        var user = CreateUser();
        user.AddSession("refresh-token", TimeSpan.FromDays(7), "device", "127.0.0.1");
        var request = CreateRequest(user.Id);
        _deletionRepository.Setup(x => x.GetByTokenHash("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _userRepository.Setup(x => x.GetById(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var (response, @event) = await Handle(
            new ConfirmAccountDeletionCommand("raw-token", AccountDeletionReason.PrivacyConcerns));

        response.IsSuccess.Should().BeTrue();
        response.Data.Status.Should().Be(AccountDeletionRequestStatus.Processing);
        request.ConfirmedAtUtc.Should().Be(Now);
        _userRepository.Verify(x => x.Delete(user, It.IsAny<CancellationToken>()), Times.Once);
        _userRepository.Verify(x => x.Update(It.IsAny<DomainUser>(), It.IsAny<CancellationToken>()), Times.Never);
        user.RefreshTokens.Should().OnlyContain(x => x.IsRevoked);
        @event.Should().BeEquivalentTo(new AccountDeletionRequestedV1(request.Id, user.Id, request.RequestedAtUtc));
        _revokedUserStore.Verify(
            x => x.RevokeAsync(user.Id, TimeSpan.FromMinutes(60), It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.Verify(
            x => x.NotifyForceLogoutAsync(
                user.Id,
                SessionTerminationReasons.UserDeleted,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAlreadyProcessing_IsIdempotentAndEnsuresRedisRevocation()
    {
        var request = CreateRequest(Guid.NewGuid());
        request.Confirm(AccountDeletionReason.NoLongerUsing, null, Now);
        _deletionRepository.Setup(x => x.GetByTokenHash("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var (response, @event) = await Handle(
            new ConfirmAccountDeletionCommand("raw-token", AccountDeletionReason.Other, "ignored"));

        response.IsSuccess.Should().BeTrue();
        response.Data.Status.Should().Be(AccountDeletionRequestStatus.Processing);
        @event.Should().BeNull();
        _userRepository.Verify(
            x => x.GetById(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _revokedUserStore.Verify(
            x => x.RevokeAsync(request.UserId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithExpiredPendingToken_ReturnsGoneWithoutMutatingUser()
    {
        var request = CreateRequest(Guid.NewGuid());
        _timeProvider.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(Now.AddHours(25)));
        _deletionRepository.Setup(x => x.GetByTokenHash("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var (response, @event) = await Handle(
            new ConfirmAccountDeletionCommand("raw-token", AccountDeletionReason.NoLongerUsing));

        response.StatusCode.Should().Be(410);
        @event.Should().BeNull();
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithCancelledToken_ReturnsConflict()
    {
        var request = CreateRequest(Guid.NewGuid());
        request.Cancel(Now);
        _deletionRepository.Setup(x => x.GetByTokenHash("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var (response, @event) = await Handle(
            new ConfirmAccountDeletionCommand("raw-token", AccountDeletionReason.NoLongerUsing));

        response.StatusCode.Should().Be(409);
        @event.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenRedisWriteFails_DoesNotReportSuccess()
    {
        var user = CreateUser();
        var request = CreateRequest(user.Id);
        _deletionRepository.Setup(x => x.GetByTokenHash("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _userRepository.Setup(x => x.GetById(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _revokedUserStore
            .Setup(x => x.RevokeAsync(user.Id, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => Handle(
            new ConfirmAccountDeletionCommand("raw-token", AccountDeletionReason.NoLongerUsing));

        await act.Should().ThrowAsync<InvalidOperationException>();
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(
            x => x.NotifyForceLogoutAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private Task<(CoffeePeek.Shared.Kernel.Response.Response<AccountDeletionRequestResponse>,
        AccountDeletionRequestedV1?)> Handle(ConfirmAccountDeletionCommand command) =>
        ConfirmAccountDeletionHandler.Handle(
            command,
            _deletionRepository.Object,
            _tokenService.Object,
            _userRepository.Object,
            _revokedUserStore.Object,
            _notifier.Object,
            _jwtOptions,
            _timeProvider.Object,
            _unitOfWork.Object,
            CancellationToken.None);

    private static DomainUser CreateUser()
    {
        var role = Role.Create("User");
        return DomainUser.Register("user@example.com", "testuser", "hash", role);
    }

    private static AccountDeletionRequest CreateRequest(Guid userId) =>
        AccountDeletionRequest.Create(
            userId,
            "hash",
            Now,
            TimeSpan.FromHours(24),
            TimeSpan.FromMinutes(5));
}
