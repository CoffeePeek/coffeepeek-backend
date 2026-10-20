using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateEmail;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.User.UpdateUserProfile.UpdateEmail;

public class UpdateEmailRequestHandlerTest
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static DomainUser CreateUser(Guid userId, string email)
    {
        var role = Role.Create("User");
        var user = DomainUser.Register(email, "testuser", "hash", role);

        var aggregateRootType = typeof(CoffeePeek.Shared.Domain.Entities.AggregateRoot<Guid>);
        var idProperty = aggregateRootType.GetProperty("Id",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        idProperty?.SetValue(user, userId);

        return user;
    }

    [Fact]
    public async Task Handle_WhenUserExists_AndEmailFree_UpdatesEmailAndReturnsSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        const string newEmail = "new@example.com";
        var command = new UpdateProfileEmailCommand(userId, newEmail);

        var user = CreateUser(userId, "old@example.com");
        _userRepositoryMock.Setup(r => r.GetById(userId, _ct)).ReturnsAsync(user);
        _userRepositoryMock.Setup(r => r.GetByEmail(newEmail, _ct)).ReturnsAsync((DomainUser?)null);

        // Act
        var (response, @event) = await UpdateEmailRequestHandler.Handle(
            command, _userRepositoryMock.Object, _unitOfWorkMock.Object, _ct);

        // Assert
        response.IsSuccess.Should().BeTrue();
        @event.UserId.Should().Be(userId);
        @event.Email.Should().Be(newEmail);
        @event.ConfirmationToken.Should().NotBeNullOrEmpty();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new UpdateProfileEmailCommand(userId, "any@example.com");
        _userRepositoryMock.Setup(r => r.GetById(userId, _ct)).ReturnsAsync((DomainUser?)null);

        // Act
        Func<Task> act = async () => await UpdateEmailRequestHandler.Handle(
            command, _userRepositoryMock.Object, _unitOfWorkMock.Object, _ct);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>().WithMessage("User not found");
    }

    [Fact]
    public async Task Handle_WhenEmailTakenByAnotherUser_ThrowsDomainException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        const string newEmail = "taken@example.com";
        var command = new UpdateProfileEmailCommand(userId, newEmail);

        var user = CreateUser(userId, "old@example.com");
        var otherUser = CreateUser(otherUserId, newEmail);
        _userRepositoryMock.Setup(r => r.GetById(userId, _ct)).ReturnsAsync(user);
        _userRepositoryMock.Setup(r => r.GetByEmail(newEmail, _ct)).ReturnsAsync(otherUser);

        // Act
        Func<Task> act = async () => await UpdateEmailRequestHandler.Handle(
            command, _userRepositoryMock.Object, _unitOfWorkMock.Object, _ct);

        // Assert
        await act.Should().ThrowAsync<DomainException>().WithMessage("Email is already taken");
    }

    [Fact]
    public async Task Handle_WhenEmailBelongsToSameUser_UpdatesSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        const string sameEmail = "same@example.com";
        var command = new UpdateProfileEmailCommand(userId, sameEmail);

        var user = CreateUser(userId, "old@example.com");
        var sameUser = CreateUser(userId, sameEmail); // GetByEmail returns user with same Id
        _userRepositoryMock.Setup(r => r.GetById(userId, _ct)).ReturnsAsync(user);
        _userRepositoryMock.Setup(r => r.GetByEmail(sameEmail, _ct)).ReturnsAsync(sameUser);

        // Act
        // existingOwner.Id == request.UserId — should NOT throw DomainException
        Func<Task> act = async () => await UpdateEmailRequestHandler.Handle(
            command, _userRepositoryMock.Object, _unitOfWorkMock.Object, _ct);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
