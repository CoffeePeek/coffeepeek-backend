using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateUsername;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Contract.Events.Account;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.User.UpdateUserProfile.UpdateUsername;

public class UpdateUsernameHandlerTest
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly CancellationToken _cancellationToken = CancellationToken.None;

    private static CoffeePeek.Account.Domain.Entities.UserAggregate.User CreateUserMock(Guid userId, string username)
    {
        // Since this is in the namespace CoffeePeek.Account.Application.Tests.Features.User..., 
        // we need to use the fully qualified name to avoid confusion with the namespace
        var user = CoffeePeek.Account.Domain.Entities.UserAggregate.User.CreateExternal("test@example.com", "external_provider", "provider_id", username);
        
        // Set the Id using reflection
        var aggregateRootType = typeof(CoffeePeek.Shared.Domain.Entities.AggregateRoot<Guid>);
        var idProperty = aggregateRootType.GetProperty("Id", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        idProperty?.SetValue(user, userId);
        
        return user;
    }
    
    [Fact]
    public async Task Handle_DuringCooldown_RejectsBeforeSaving()
    {
        var user = CreateUserMock(Guid.NewGuid(), "Original");
        user.UpdateUsername(Username.Create("Changed"));
        _userRepositoryMock.Setup(x => x.GetById(user.Id, _cancellationToken)).ReturnsAsync(user);
        await Assert.ThrowsAsync<ConflictException>(() => UpdateUsernameHandler.Handle(new(user.Id, "Rejected"),
            _userRepositoryMock.Object, _unitOfWorkMock.Object, _cancellationToken));
        _userRepositoryMock.Verify(x => x.Update(It.IsAny<DomainUser>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithValidUserAndUsername_ShouldUpdateUsernameSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        const string newUsername = "newusername";
        var command = new UpdateProfileUsernameCommand(userId, newUsername);
        
        var existingUser = CreateUserMock(userId, "oldusername");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        var (response, @event) = await UpdateUsernameHandler.Handle(
            command,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cancellationToken);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(newUsername);

        @event.Should().NotBeNull();
        @event.NewUserName.Should().Be(newUsername);
        @event.UserId.Should().Be(userId);

        existingUser.Username.Value.Should().Be(newUsername);
        
        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_WithNonExistingUser_ShouldThrowNotFoundException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new UpdateProfileUsernameCommand(userId, "newusername");

        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync((Domain.Entities.UserAggregate.User)null!);

        // Act
        Func<Task> act = async () => await UpdateUsernameHandler.Handle(
            command,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("User not found");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);

    }

    [Fact]
    public async Task Handle_WithInvalidUsername_ShouldThrowDomainException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        const string invalidUsername = ""; 
        var command = new UpdateProfileUsernameCommand(userId, invalidUsername);

        var existingUser = CreateUserMock(userId, "oldusername");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        Func<Task> act = async () => await UpdateUsernameHandler.Handle(
            command,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Username cannot be empty.");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_WithTooShortUsername_ShouldThrowDomainException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        const string tooShortUsername = "ab"; 
        var command = new UpdateProfileUsernameCommand(userId, tooShortUsername);

        var existingUser = CreateUserMock(userId, "oldusername");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        Func<Task> act = async () => await UpdateUsernameHandler.Handle(
            command,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Username must be between 3 and 30 characters.");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_WithTooLongUsername_ShouldThrowDomainException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tooLongUsername = new string('a', 31); 
        var command = new UpdateProfileUsernameCommand(userId, tooLongUsername);

        var existingUser = CreateUserMock(userId, "oldusername");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        Func<Task> act = async () => await UpdateUsernameHandler.Handle(
            command,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Username must be between 3 and 30 characters.");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_WithInvalidCharactersInUsername_ShouldThrowDomainException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var invalidUsername = "user@invalid"; 
        var command = new UpdateProfileUsernameCommand(userId, invalidUsername);

        var existingUser = CreateUserMock(userId, "oldusername");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        Func<Task> act = async () => await UpdateUsernameHandler.Handle(
            command,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Username can only contain letters, numbers, dots, and underscores, and must start with a letter.");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_WithSameUsername_ShouldStillUpdateSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sameUsername = "sameusername";
        var command = new UpdateProfileUsernameCommand(userId, sameUsername);

        var existingUser = CreateUserMock(userId, sameUsername);
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        var (response, @event) = await UpdateUsernameHandler.Handle(
            command,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cancellationToken);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(sameUsername);

        @event.Should().NotBeNull();
        @event.NewUserName.Should().Be(sameUsername);

        existingUser.Username.Value.Should().Be(sameUsername);

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_WithDifferentCasingUsername_ShouldUpdateSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var newUsername = "NewUsername"; // Case change only
        var command = new UpdateProfileUsernameCommand(userId, newUsername);

        var existingUser = CreateUserMock(userId, "oldusername");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        var (response, @event) = await UpdateUsernameHandler.Handle(
            command,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cancellationToken);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(newUsername);

        @event.Should().NotBeNull();
        @event.NewUserName.Should().Be(newUsername);

        existingUser.Username.Value.Should().Be(newUsername);

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
    }

    [Theory]
    [InlineData("validuser")]
    [InlineData("user.name")]
    [InlineData("user_name")]
    [InlineData("user.name123")]
    [InlineData("user_name_123")]
    [InlineData("Кофеман")]
    [InlineData("Иван_Иванов")]
    [InlineData("a12345678901234567890123456789")] // 30 chars
    public async Task Handle_WithValidUsernameFormats_ShouldUpdateSuccessfully(string validUsername)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new UpdateProfileUsernameCommand(userId, validUsername);

        var existingUser = CreateUserMock(userId, "oldusername");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        var (response, @event) = await UpdateUsernameHandler.Handle(
            command,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cancellationToken);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(validUsername);

        @event.Should().NotBeNull();
        @event.NewUserName.Should().Be(validUsername);

        existingUser.Username.Value.Should().Be(validUsername);

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
    }

    [Theory]
    [InlineData("123user")]   // Starts with a digit
    [InlineData(".username")]  // Starts with a dot
    [InlineData("_username")]  // Starts with an underscore
    [InlineData("user@name")]  // Forbidden character @
    [InlineData("user name")]  // Space
    [InlineData("user-name")]  // Hyphen (if forbidden)
    public async Task Handle_WithInvalidUsernameFormats_ShouldThrowDomainException(string invalidUsername)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new UpdateProfileUsernameCommand(userId, invalidUsername);

        var existingUser = CreateUserMock(userId, "oldusername");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        Func<Task> act = async () => await UpdateUsernameHandler.Handle(
            command,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<DomainException>();

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
    }
}
