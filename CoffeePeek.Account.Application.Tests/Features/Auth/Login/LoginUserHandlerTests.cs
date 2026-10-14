using CoffeePeek.Account.Application.Common;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Common.Models;
using CoffeePeek.Account.Application.Features.Auth.Login;
using CoffeePeek.Shared.Kernel.Exceptions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Moq;

namespace CoffeePeek.Account.Application.Tests.Features.Auth.Login;

public class LoginUserHandlerTests
{
    private readonly Mock<IAuthService> _authServiceMock = new();
    private readonly EmailExistenceFilter _filter = new(1000, 0.01);
    private readonly CancellationToken _ct = CancellationToken.None;

    private static LoginUserCommand CreateCommand(string email = "user@example.com", string password = "password123") =>
        new(email, password, "Chrome", "127.0.0.1");

    [Fact]
    public void Command_ToString_DoesNotExposeSubmittedPassword()
    {
        const string password = "secret, with } delimiters and spaces";
        var command = CreateCommand(password: password);

        command.ToString().Should().Contain("[REDACTED]");
        command.ToString().Should().NotContain(password);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_ReturnsLoginResponse()
    {
        // Arrange
        var command = CreateCommand();
        _authServiceMock.Setup(s => s.LoginAsync(command.Email, command.Password, command.DeviceName, command.IpAddress))
            .ReturnsAsync(new AuthResult { AccessToken = "access", RefreshToken = "refresh", ExpiredAt = DateTime.UtcNow.AddMinutes(15) });

        // Act
        var response = await LoginUserHandler.Handle(command, _authServiceMock.Object, _filter, _ct);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.AccessToken.Should().Be("access");
        response.Data.RefreshToken.Should().Be("refresh");
    }

    [Fact]
    public async Task Handle_WithValidCredentials_AddsEmailToFilter()
    {
        // Arrange
        var command = CreateCommand("new@example.com");
        _authServiceMock.Setup(s => s.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new AuthResult { AccessToken = "a", RefreshToken = "r", ExpiredAt = DateTime.UtcNow.AddMinutes(15) });

        _filter.MightExist("new@example.com").Should().BeFalse("email not added yet");

        // Act
        await LoginUserHandler.Handle(command, _authServiceMock.Object, _filter, _ct);

        // Assert
        _filter.MightExist("new@example.com").Should().BeTrue("email added after login");
    }

    [Fact]
    public async Task Handle_WhenAuthServiceThrowsUnauthorized_PropagatesException()
    {
        // Arrange
        var command = CreateCommand();
        _authServiceMock.Setup(s => s.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new UnauthorizedException("Invalid credentials"));

        // Act
        Func<Task> act = () => LoginUserHandler.Handle(command, _authServiceMock.Object, _filter, _ct);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid credentials");
    }

    [Fact]
    public async Task Handle_WhenAuthServiceThrowsUnauthorized_DoesNotAddEmailToFilter()
    {
        // Arrange
        var command = CreateCommand("ghost@example.com");
        _authServiceMock.Setup(s => s.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new UnauthorizedException("Invalid credentials"));

        // Act
        try { await LoginUserHandler.Handle(command, _authServiceMock.Object, _filter, _ct); } catch { }

        // Assert — filter must not be poisoned by failed login
        _filter.MightExist("ghost@example.com").Should().BeFalse();
    }
}
