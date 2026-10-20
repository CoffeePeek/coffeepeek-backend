using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdatePhoneNumber;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.User.UpdateUserProfile.UpdatePhoneNumber;

public class UpdatePhoneNumberHandlerTest
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly CancellationToken _cancellationToken = CancellationToken.None;

    [Fact]
    public async Task Handle_WithValidUserAndPhoneNumber_ShouldUpdatePhoneNumberSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var newPhoneNumber = "+375291234567";
        var command = new UpdateProfilePhoneNumberCommand(userId, newPhoneNumber);
        
        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);  // Return 1 to indicate one record was saved

        // Act
        var result = await UpdatePhoneNumberHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(newPhoneNumber);
        result.Message.Should().Be("Phone number updated successful");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);

        existingUser.PhoneNumber!.Value.Should().Be(newPhoneNumber);
    }

    [Fact]
    public async Task Handle_WithNonExistingUser_ShouldThrowNotFoundException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var phoneNumber = "+375291234567";
        var command = new UpdateProfilePhoneNumberCommand(userId, phoneNumber);

        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync((CoffeePeek.Account.Domain.Entities.UserAggregate.User)null);

        // Act
        Func<Task> act = async () => await UpdatePhoneNumberHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("User not found");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidPhoneNumber_ShouldThrowDomainException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var invalidPhoneNumber = ""; // Invalid phone number (empty)
        var command = new UpdateProfilePhoneNumberCommand(userId, invalidPhoneNumber);

        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        Func<Task> act = async () => await UpdatePhoneNumberHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Phone number cannot be empty.");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidPhoneNumberFormat_ShouldThrowDomainException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var invalidPhoneNumber = "invalid_phone"; // Invalid phone number format
        var command = new UpdateProfilePhoneNumberCommand(userId, invalidPhoneNumber);

        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        Func<Task> act = async () => await UpdatePhoneNumberHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Invalid Belarusian phone number format. Expected 375XXXXXXXXX or 80XXXXXXXXX");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Never);
    }

    [Fact]
    public async Task Handle_WithDifferentValidPhoneFormats_ShouldNormalizeAndAccept()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var phoneNumbers = new[]
        {
            "80291234567",      // Domestic format
            "375291234567",     // International format without +
            "+375291234567",    // Full international format
            "8029 123-45-67",   // With spaces and dashes
            "+375 (29) 123-45-67" // Formatted
        };

        foreach (var phoneNumber in phoneNumbers)
        {
            var command = new UpdateProfilePhoneNumberCommand(userId, phoneNumber);

            var existingUser = CreateUserMock(userId, "testuser");
            var repoMock = new Mock<IUserRepository>();
            var unitOfWorkMock = new Mock<IUnitOfWork>();

            repoMock.Setup(repo => repo.GetById(userId, _cancellationToken))
                .ReturnsAsync(existingUser);
            unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
                .ReturnsAsync(1);

            // Act
            var result = await UpdatePhoneNumberHandler.Handle(command, repoMock.Object, 
                unitOfWorkMock.Object, _cancellationToken);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Data.Should().StartWith("+375"); // Normalized format should start with +375
            
            repoMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
            unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);
        }
    }

    [Fact]
    public async Task Handle_WithSamePhoneNumber_ShouldStillUpdateSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var existingPhoneNumber = "+375291234567";
        var command = new UpdateProfilePhoneNumberCommand(userId, existingPhoneNumber);

        var existingUser = CreateUserMock(userId, "testuser");
        existingUser.UpdatePhoneNumber(PhoneNumber.Create(existingPhoneNumber)); // Set initial phone number
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);  // Return 1 to indicate one record was saved

        // Act
        var result = await UpdatePhoneNumberHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(existingPhoneNumber);
        result.Message.Should().Be("Phone number updated successful");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);

        existingUser.PhoneNumber!.Value.Should().Be(existingPhoneNumber);
    }

    [Fact]
    public async Task Handle_UnitOfWorkSaveChangesThrowsException_ShouldPropagateException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var phoneNumber = "+375291234567";
        var command = new UpdateProfilePhoneNumberCommand(userId, phoneNumber);

        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        var exception = new Exception("Database error");
        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ThrowsAsync(exception);

        // Act
        Func<Task> act = async () => await UpdatePhoneNumberHandler.Handle(command, _userRepositoryMock.Object, 
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
        var phoneNumber = "+375291234567";
        var command = new UpdateProfilePhoneNumberCommand(userId, phoneNumber);

        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        var exception = new Exception("Repository error");
        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ThrowsAsync(exception);

        // Act
        Func<Task> act = async () => await UpdatePhoneNumberHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<Exception>()
            .WithMessage("Repository error");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);
    }

    [Theory]
    [InlineData("+375291234567")]  // A1
    [InlineData("+375331234567")]  // MTS
    [InlineData("+375441234567")]  // A1
    [InlineData("+375251234567")]  // Life:)
    [InlineData("+375171234567")]  // Beltelecom
    public async Task Handle_WithValidBelarusianPhoneNumbers_ShouldUpdateSuccessfully(string validPhoneNumber)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new UpdateProfilePhoneNumberCommand(userId, validPhoneNumber);

        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        _unitOfWorkMock.Setup(uow => uow.SaveChangesAsync(_cancellationToken))
            .ReturnsAsync(1);  // Return 1 to indicate one record was saved

        // Act
        var result = await UpdatePhoneNumberHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(validPhoneNumber);
        result.Message.Should().Be("Phone number updated successful");

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Once);

        existingUser.PhoneNumber!.Value.Should().Be(validPhoneNumber);
    }

    [Theory]
    [InlineData("12345")]                    // Too short
    [InlineData("12345678901234567890")]    // Too long
    [InlineData("+1234567890")]             // Wrong country code
    [InlineData("375")]                     // Incomplete
    [InlineData("abc")]                     // Letters only
    [InlineData("+442079460000")]           // UK number (not Belarusian)
    public async Task Handle_WithInvalidPhoneNumbers_ShouldThrowDomainException(string invalidPhoneNumber)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new UpdateProfilePhoneNumberCommand(userId, invalidPhoneNumber);

        var existingUser = CreateUserMock(userId, "testuser");
        _userRepositoryMock.Setup(repo => repo.GetById(userId, _cancellationToken))
            .ReturnsAsync(existingUser);

        // Act
        Func<Task> act = async () => await UpdatePhoneNumberHandler.Handle(command, _userRepositoryMock.Object, 
            _unitOfWorkMock.Object, _cancellationToken);

        // Assert
        await act.Should().ThrowAsync<DomainException>();

        _userRepositoryMock.Verify(repo => repo.GetById(userId, _cancellationToken), Times.Once);
        _unitOfWorkMock.Verify(uow => uow.SaveChangesAsync(_cancellationToken), Times.Never);
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