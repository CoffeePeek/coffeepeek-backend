using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateAbout;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.User.UpdateUserProfile.UpdateAbout;

public class UpdateProfileHandlerTest
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly CancellationToken _cancellationToken = CancellationToken.None;

    [Fact]
    public async Task Handle_WithValidUserAndAbout_ShouldUpdateProfileSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var aboutText = "This is my profile description";
        var command = new UpdateProfileAboutCommand(userId, aboutText);
        
        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);  // Return 1 to indicate one record was saved

        // Act
        var result = await UpdateProfileHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(aboutText);
        result.Message.Should().Be("Profile updated successfully");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);

        existingUser.About.Should().Be(aboutText);
    }

    [Fact]
    public async Task Handle_WithNullAbout_ShouldUpdateProfileToNull()
    {
        // Arrange
        var userId = Guid.NewGuid();
        string aboutText = null; // Null about text
        var command = new UpdateProfileAboutCommand(userId, aboutText);
        
        var existingUser = CreateUserMock(userId, "testuser");
        existingUser.UpdateAbout("Initial about text"); // Set initial about text
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);  // Return 1 to indicate one record was saved

        // Act
        var result = await UpdateProfileHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeNull();
        result.Message.Should().Be("Profile updated successfully");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);

        existingUser.About.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithEmptyAbout_ShouldUpdateProfileToEmpty()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var aboutText = "";
        var command = new UpdateProfileAboutCommand(userId, aboutText);
        
        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);  // Return 1 to indicate one record was saved

        // Act
        var result = await UpdateProfileHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be("");
        result.Message.Should().Be("Profile updated successfully");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);

        existingUser.About.Should().Be("");
    }

    [Fact]
    public async Task Handle_WithWhitespaceOnlyAbout_ShouldUpdateProfileToWhitespace()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var aboutText = "   \t\n   "; // Whitespace only
        var command = new UpdateProfileAboutCommand(userId, aboutText);
        
        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);  // Return 1 to indicate one record was saved

        // Act
        var result = await UpdateProfileHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be("   \t\n   ");
        result.Message.Should().Be("Profile updated successfully");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);

        existingUser.About.Should().Be("   \t\n   ");
    }

    [Fact]
    public async Task Handle_WithLongAboutText_ShouldUpdateProfileSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var aboutText = new string('a', 1000); // Long text
        var command = new UpdateProfileAboutCommand(userId, aboutText);
        
        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);  // Return 1 to indicate one record was saved

        // Act
        var result = await UpdateProfileHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(aboutText);
        result.Message.Should().Be("Profile updated successfully");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);

        existingUser.About.Should().Be(aboutText);
    }

    [Fact]
    public async Task Handle_WithNonExistingUser_ShouldThrowNotFoundException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var aboutText = "This is my profile description";
        var command = new UpdateProfileAboutCommand(userId, aboutText);

        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync((CoffeePeek.Account.Domain.Entities.UserAggregate.User)null);

        // Act
        Func<Task> act = async () => await UpdateProfileHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("User not found");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Never);
    }

    [Fact]
    public async Task Handle_WithSameAboutText_ShouldUpdateProfileSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var aboutText = "Existing about text";
        var command = new UpdateProfileAboutCommand(userId, aboutText);

        var existingUser = CreateUserMock(userId, "testuser");
        existingUser.UpdateAbout(aboutText); // Set same about text
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);  // Return 1 to indicate one record was saved

        // Act
        var result = await UpdateProfileHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(aboutText);
        result.Message.Should().Be("Profile updated successfully");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);

        existingUser.About.Should().Be(aboutText);
    }

    [Fact]
    public async Task Handle_UnitOfWorkSaveChangesThrowsException_ShouldPropagateException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var aboutText = "This is my profile description";
        var command = new UpdateProfileAboutCommand(userId, aboutText);

        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        var exception = new Exception("Database error");
        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ThrowsAsync(exception);

        // Act
        Func<Task> act = async () => await UpdateProfileHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Database error");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);
    }

    [Fact]
    public async Task Handle_UserRepositoryUpdateThrowsException_ShouldPropagateException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var aboutText = "This is my profile description";
        var command = new UpdateProfileAboutCommand(userId, aboutText);

        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        var exception = new Exception("Repository error");
        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ThrowsAsync(exception);

        // Act
        Func<Task> act = async () => await UpdateProfileHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Repository error");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);
    }

    [Theory]
    [InlineData("Simple text")]
    [InlineData("Text with numbers 12345")]
    [InlineData("Text with symbols !@#$%^&*()")]
    [InlineData("Text with unicode: Привет, 你好,こんにちは")]
    [InlineData("Multiple\nlines\nof\ntext")]
    [InlineData("Tab\tdelimited\ttext")]
    [InlineData("")]
    [InlineData("a")] // Single character
    [InlineData("   ")] // Spaces only
    public async Task Handle_WithVariousAboutTexts_ShouldUpdateSuccessfully(string aboutText)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new UpdateProfileAboutCommand(userId, aboutText);

        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);  // Return 1 to indicate one record was saved

        // Act
        var result = await UpdateProfileHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(aboutText);
        result.Message.Should().Be("Profile updated successfully");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);

        existingUser.About.Should().Be(aboutText);
    }

    private CoffeePeek.Account.Domain.Entities.UserAggregate.User CreateUserMock(Guid userId, string username)
    {
        // Since this is in the namespace CoffeePeek.Account.Application.Tests.Features.User..., 
        // we need to use the fully qualified name to avoid confusion with the namespace
        var user = CoffeePeek.Account.Domain.Entities.UserAggregate.User.CreateExternal("test@example.com", "external_provider", "provider_id");
        user.UpdateUsername(Username.Create(username));
        
        // Set the Id using reflection
        var aggregateRootType = typeof(CoffeePeek.Shared.Domain.Entities.AggregateRoot<Guid>);
        var idProperty = aggregateRootType.GetProperty("Id", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        idProperty?.SetValue(user, userId);
        
        return user;
    }
}