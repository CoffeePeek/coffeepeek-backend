using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public class UsernameTests
{
    [Theory]
    [InlineData("user123")]
    [InlineData("john_doe")]
    [InlineData("alice.smith")]
    [InlineData("User123")] // Uppercase allowed
    [InlineData("a1b2c3")]
    [InlineData("testuser")]
    [InlineData("User_Name")]
    [InlineData("user.name")]
    [InlineData("Иван")]
    [InlineData("кофеман_123")]
    [InlineData("Анна.Мария")]
    public void Create_WithValidUsername_ShouldSucceed(string validUsername)
    {
        // Act
        var username = Username.Create(validUsername);

        // Assert
        username.Should().NotBeNull();
        username.Value.Should().Be(validUsername);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespace_ShouldThrowDomainException(string? invalidUsername)
    {
        // Act
        Action act = () => Username.Create(invalidUsername!);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*Username*empty*");
    }

    [Fact]
    public void Create_WithUsernameTooShort_ShouldThrowDomainException()
    {
        // Arrange - less than 3 characters
        const string shortUsername = "ab";

        // Act
        Action act = () => Username.Create(shortUsername);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*between*3*");
    }

    [Fact]
    public void Create_WithUsernameTooLong_ShouldThrowDomainException()
    {
        // Arrange - more than MaxLength (30)
        var longUsername = new string('a', 31);

        // Act
        Action act = () => Username.Create(longUsername);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*between*30*");
    }

    [Theory]
    [InlineData("user name")] // Contains space
    [InlineData("user@name")] // Contains @
    [InlineData("user#name")] // Contains #
    [InlineData("user$name")] // Contains $
    [InlineData("user%name")] // Contains %
    [InlineData("user&name")] // Contains &
    [InlineData("user*name")] // Contains *
    [InlineData("user-name")] // Contains dash (not allowed based on regex)
    [InlineData("123user")] // Starts with number
    [InlineData("_username")] // Starts with underscore
    [InlineData(".username")] // Starts with dot
    public void Create_WithInvalidCharactersOrFormat_ShouldThrowDomainException(string invalidUsername)
    {
        // Act
        Action act = () => Username.Create(invalidUsername);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*can only contain letters*");
    }

    [Fact]
    public void Create_WithMinimumValidLength_ShouldSucceed()
    {
        // Arrange - exactly 3 characters
        const string minUsername = "abc";

        // Act
        var username = Username.Create(minUsername);

        // Assert
        username.Value.Should().Be("abc");
        username.Value.Length.Should().Be(3);
    }

    [Fact]
    public void Create_WithMaximumValidLength_ShouldSucceed()
    {
        // Arrange - exactly 30 characters
        var maxUsername = "a" + new string('b', 29);

        // Act
        var username = Username.Create(maxUsername);

        // Assert
        username.Value.Should().Be(maxUsername);
        username.Value.Length.Should().Be(30);
    }

    [Fact]
    public void Create_ShouldTrimWhitespace()
    {
        // Arrange
        const string usernameWithSpaces = "  testuser  ";

        // Act
        var username = Username.Create(usernameWithSpaces);

        // Assert
        username.Value.Should().Be("testuser");
    }

    [Fact]
    public void ImplicitConversion_FromUsername_ShouldReturnValue()
    {
        // Arrange
        var username = Username.Create("testuser");

        // Act
        string value = username;

        // Assert
        value.Should().Be("testuser");
    }

    [Fact]
    public void ToString_ShouldReturnValue()
    {
        // Arrange
        var username = Username.Create("testuser");

        // Act
        var result = username.ToString();

        // Assert
        result.Should().Be("testuser");
    }

    [Fact]
    public void Equals_WithSameUsername_ShouldReturnTrue()
    {
        // Arrange
        var username1 = Username.Create("testuser");
        var username2 = Username.Create("testuser");

        // Act & Assert
        username1.Equals(username2).Should().BeTrue();
        (username1 == username2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithDifferentUsername_ShouldReturnFalse()
    {
        // Arrange
        var username1 = Username.Create("user1");
        var username2 = Username.Create("user2");

        // Act & Assert
        username1.Equals(username2).Should().BeFalse();
        (username1 != username2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_WithSameUsername_ShouldReturnSameHash()
    {
        // Arrange
        var username1 = Username.Create("testuser");
        var username2 = Username.Create("testuser");

        // Act & Assert
        username1.GetHashCode().Should().Be(username2.GetHashCode());
    }

    [Theory]
    [InlineData("user_123")]
    [InlineData("user.123")]
    [InlineData("User123")]
    [InlineData("UserName")]
    [InlineData("Пользователь_123")]
    public void Create_WithAllowedSpecialCharacters_ShouldSucceed(string username)
    {
        // Act
        var result = Username.Create(username);

        // Assert
        result.Value.Should().Be(username);
    }

    [Fact]
    public void Create_MustStartWithLetter()
    {
        // Arrange - must start with letter per regex
        const string validUsername = "a123";
        const string invalidUsername = "1abc";

        // Act
        var valid = Username.Create(validUsername);
        Action invalid = () => Username.Create(invalidUsername);

        // Assert
        valid.Value.Should().Be(validUsername);
        invalid.Should().Throw<DomainException>();
    }
}
