using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.Auth.Email.ResendEmailConfirmation;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Auth.Email;

public class ResendEmailConfirmationHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static DomainUser CreateUnconfirmedUser()
    {
        var role = Role.Create("User");
        return DomainUser.Register("user@example.com", "testuser", "hash", role);
    }

    private static DomainUser CreateConfirmedUser()
    {
        var role = Role.Create("User");
        var user = DomainUser.Register("user@example.com", "testuser", "hash", role);
        typeof(UserCredential).GetProperty(nameof(UserCredential.EmailConfirmed))!
            .SetValue(user.Credentials, true);
        return user;
    }

    [Fact]
    public async Task Handle_WithUnconfirmedUser_ReturnsSuccessAndEvent()
    {
        var user = CreateUnconfirmedUser();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        var (response, @event) = await ResendEmailConfirmationHandler.Handle(
            new ResendEmailConfirmationCommand(user.Id), _userRepoMock.Object, _unitOfWorkMock.Object, _ct);
        response.IsSuccess.Should().BeTrue();
        @event.Email.Should().Be("user@example.com");
        @event.ConfirmationToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_WithUnconfirmedUser_GeneratesNewToken()
    {
        var user = CreateUnconfirmedUser();
        var originalToken = user.Credentials.EmailConfirmationToken;
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        var (_, @event) = await ResendEmailConfirmationHandler.Handle(
            new ResendEmailConfirmationCommand(user.Id), _userRepoMock.Object, _unitOfWorkMock.Object, _ct);
        @event.ConfirmationToken.Should().NotBe(originalToken);
    }

    [Fact]
    public async Task Handle_WithUnconfirmedUser_CallsSaveChanges()
    {
        var user = CreateUnconfirmedUser();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        await ResendEmailConfirmationHandler.Handle(
            new ResendEmailConfirmationCommand(user.Id), _userRepoMock.Object, _unitOfWorkMock.Object, _ct);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        var userId = Guid.NewGuid();
        _userRepoMock.Setup(r => r.GetById(userId, _ct)).ReturnsAsync((DomainUser?)null);
        Func<Task> act = () => ResendEmailConfirmationHandler.Handle(
            new ResendEmailConfirmationCommand(userId), _userRepoMock.Object, _unitOfWorkMock.Object, _ct);
        await act.Should().ThrowAsync<NotFoundException>().WithMessage("User not found");
    }

    [Fact]
    public async Task Handle_WhenEmailAlreadyConfirmed_ThrowsDomainException()
    {
        var user = CreateConfirmedUser();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        Func<Task> act = () => ResendEmailConfirmationHandler.Handle(
            new ResendEmailConfirmationCommand(user.Id), _userRepoMock.Object, _unitOfWorkMock.Object, _ct);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*already confirmed*");
    }
}
