using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Common;
using CoffeePeek.Account.Application.Features.Auth.RegisterUser;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Auth.Register;

public class RegisterUserHandlerTests
{
    private readonly Mock<IQueryUserRepository> _queryRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly Mock<IPasswordHasherService> _hasherMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly EmailExistenceFilter _filter = new(1000, 0.01);
    private readonly CancellationToken _ct = CancellationToken.None;

    private RegisterUserCommand CreateCommand(string email = "user@example.com", string username = "testuser", string password = "password123") =>
        new(username, email, password);

    public RegisterUserHandlerTests()
    {
        _queryRepoMock.Setup(r => r.IsEmailUnique(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _queryRepoMock.Setup(r => r.IsUsernameUnique(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _roleRepoMock.Setup(r => r.GetRoleAsync(RoleConsts.User)).ReturnsAsync(Role.Create(RoleConsts.User));
        _hasherMock.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hashed_password");
    }

    [Fact]
    public async Task Handle_WithValidData_ReturnsSuccessAndEvent()
    {
        var command = CreateCommand();
        var (response, @event) = await RegisterUserHandler.Handle(command, _queryRepoMock.Object, _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);
        response.IsSuccess.Should().BeTrue();
        @event.Email.Should().Be(command.Email);
        @event.ConfirmationToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_WithCyrillicUsername_ReturnsSuccess()
    {
        var command = CreateCommand(username: "Кофеман");

        var (response, @event) = await RegisterUserHandler.Handle(command, _queryRepoMock.Object,
            _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        @event.Username.Should().Be(command.UserName);
        _queryRepoMock.Verify(r => r.Add(It.Is<DomainUser>(u => u.Username.Value == command.UserName), _ct),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidData_AddsUserToRepository()
    {
        await RegisterUserHandler.Handle(CreateCommand(), _queryRepoMock.Object, _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);
        _queryRepoMock.Verify(r => r.Add(It.IsAny<DomainUser>(), _ct), Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidData_CallsSaveChanges()
    {
        await RegisterUserHandler.Handle(CreateCommand(), _queryRepoMock.Object, _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmailInBloomFilter_StillChecksUniquenessAndSucceeds()
    {
        const string email = "taken@example.com";
        _filter.Add(email);
        var (response, @event) = await RegisterUserHandler.Handle(CreateCommand(email: email), _queryRepoMock.Object, _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);
        response.IsSuccess.Should().BeTrue();
        @event.Email.Should().Be(email);
        _queryRepoMock.Verify(r => r.IsEmailUnique(email, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUsernameNotUniqueInDb_ThrowsDomainException()
    {
        _queryRepoMock.Setup(r => r.IsUsernameUnique(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Func<Task> act = () => RegisterUserHandler.Handle(CreateCommand(), _queryRepoMock.Object, _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);
        await act.Should().ThrowAsync<DomainException>().WithMessage("Username already exists");
    }

    [Fact]
    public async Task Handle_WhenEmailNotUniqueInDb_ThrowsDomainException()
    {
        _queryRepoMock.Setup(r => r.IsEmailUnique(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Func<Task> act = () => RegisterUserHandler.Handle(CreateCommand(), _queryRepoMock.Object, _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);
        await act.Should().ThrowAsync<DomainException>().WithMessage("Email already exists");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("1234567")]
    public async Task Handle_WithPasswordTooShort_ThrowsDomainException(string shortPassword)
    {
        Func<Task> act = () => RegisterUserHandler.Handle(CreateCommand(password: shortPassword), _queryRepoMock.Object, _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);
        await act.Should().ThrowAsync<DomainException>().WithMessage("Password must be at least 8 characters long");
        _hasherMock.Verify(h => h.HashPassword(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenDefaultRoleNotFound_ThrowsDomainException()
    {
        _roleRepoMock.Setup(r => r.GetRoleAsync(RoleConsts.User)).ReturnsAsync((Role?)null);
        Func<Task> act = () => RegisterUserHandler.Handle(CreateCommand(), _queryRepoMock.Object, _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);
        await act.Should().ThrowAsync<DomainException>().WithMessage("Default role not found");
    }

    [Fact]
    public async Task Handle_WhenRaceCondition_ThrowsDomainException()
    {
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(_ct)).ThrowsAsync(new CoffeePeek.Shared.Kernel.Exceptions.ConflictException("conflict"));
        Func<Task> act = () => RegisterUserHandler.Handle(CreateCommand(), _queryRepoMock.Object, _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);
        await act.Should().ThrowAsync<DomainException>().WithMessage("Email already exists");
    }

    [Fact]
    public async Task Handle_WithValidData_AddsEmailToFilter()
    {
        const string email = "fresh@example.com";
        _filter.MightExist(email).Should().BeFalse();
        await RegisterUserHandler.Handle(CreateCommand(email: email), _queryRepoMock.Object, _roleRepoMock.Object, _hasherMock.Object, _filter, _unitOfWorkMock.Object, _ct);
        _filter.MightExist(email).Should().BeTrue();
    }
}
