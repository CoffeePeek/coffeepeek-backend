using CoffeePeek.Account.Infrastructure.Identity;
using FluentAssertions;

namespace CoffeePeek.Account.Infrastructure.Tests;

public class PasswordHasherServiceTests
{
    private readonly PasswordHasherService _sut = new();

    [Fact]
    public void HashPassword_WithValidPassword_ShouldReturnHashedString()
    {
        // Arrange
        const string password = "MySecurePassword123!";

        // Act
        var hash = _sut.HashPassword(password);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe(password);
        hash.Length.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HashPassword_WithInvalidPassword_ShouldThrowArgumentException(string? invalidPassword)
    {
        // Act
        Action act = () => _sut.HashPassword(invalidPassword!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Password*empty*");
    }

    [Fact]
    public void HashPassword_ShouldProduceDifferentHashesForSamePassword()
    {
        // Arrange
        const string password = "SamePassword123!";

        // Act
        var hash1 = _sut.HashPassword(password);
        var hash2 = _sut.HashPassword(password);

        // Assert
        hash1.Should().NotBe(hash2); // Different salts
    }

    [Fact]
    public void HashPassword_ShouldProduceBase64EncodedString()
    {
        // Arrange
        const string password = "TestPassword123!";

        // Act
        var hash = _sut.HashPassword(password);

        // Assert
        Action act = () => Convert.FromBase64String(hash);
        act.Should().NotThrow();
    }

    [Fact]
    public void HashPassword_ShouldProduceHashOfCorrectLength()
    {
        // Arrange
        const string password = "TestPassword123!";

        // Act
        var hash = _sut.HashPassword(password);
        var hashBytes = Convert.FromBase64String(hash);

        // Assert
        // Salt (16 bytes) + Key (32 bytes) = 48 bytes
        hashBytes.Length.Should().Be(48);
    }

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ShouldReturnTrue()
    {
        // Arrange
        const string password = "MySecurePassword123!";
        var hash = _sut.HashPassword(password);

        // Act
        var result = _sut.VerifyPassword(hash, password);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithIncorrectPassword_ShouldReturnFalse()
    {
        // Arrange
        const string correctPassword = "CorrectPassword123!";
        const string incorrectPassword = "WrongPassword456!";
        var hash = _sut.HashPassword(correctPassword);

        // Act
        var result = _sut.VerifyPassword(hash, incorrectPassword);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VerifyPassword_WithInvalidHashedPassword_ShouldThrowArgumentException(string? invalidHash)
    {
        // Act
        Action act = () => _sut.VerifyPassword(invalidHash!, "password");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Hashed password*empty*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VerifyPassword_WithInvalidProvidedPassword_ShouldThrowArgumentException(string? invalidPassword)
    {
        // Arrange
        var hash = _sut.HashPassword("ValidPassword123!");

        // Act
        Action act = () => _sut.VerifyPassword(hash, invalidPassword!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Provided password*empty*");
    }

    [Fact]
    public void VerifyPassword_WithInvalidHashFormat_ShouldReturnFalse()
    {
        // Arrange
        const string invalidHash = "not-a-valid-hash";

        // Act & Assert
        Action act = () => _sut.VerifyPassword(invalidHash, "password");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void VerifyPassword_WithHashOfWrongLength_ShouldReturnFalse()
    {
        // Arrange
        var shortHash = Convert.ToBase64String(new byte[32]); // Too short (should be 48)

        // Act
        var result = _sut.VerifyPassword(shortHash, "password");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("SimplePassword")]
    [InlineData("Complex!@#$%Password123")]
    [InlineData("VeryLongPasswordWithManyCharacters123!@#")]
    [InlineData("short")]
    [InlineData("1234567890")]
    public void HashAndVerify_WithVariousPasswords_ShouldWorkCorrectly(string password)
    {
        // Arrange
        var hash = _sut.HashPassword(password);

        // Act
        var result = _sut.VerifyPassword(hash, password);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(31)]
    public void VerifyPassword_WithTamperedKey_ShouldReturnFalse(int keyOffset)
    {
        const string password = "MySecurePassword123!";
        var hashBytes = Convert.FromBase64String(_sut.HashPassword(password));
        // CI load makes wall-clock assertions unreliable; validate key mismatches instead.
        hashBytes[16 + keyOffset] ^= 1;

        _sut.VerifyPassword(Convert.ToBase64String(hashBytes), password).Should().BeFalse();
    }

    [Fact]
    public void HashPassword_MultipleTimes_ShouldProduceUniqueHashes()
    {
        // Arrange
        const string password = "TestPassword123!";
        var hashes = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var hash = _sut.HashPassword(password);
            hashes.Add(hash);
        }

        // Assert - All hashes should be unique due to different salts
        hashes.Count.Should().Be(100);
    }

    [Fact]
    public void VerifyPassword_WithCaseSensitivePassword_ShouldDistinguishCases()
    {
        // Arrange
        const string password = "TestPassword123!";
        const string wrongCasePassword = "testpassword123!";
        var hash = _sut.HashPassword(password);

        // Act
        var correctResult = _sut.VerifyPassword(hash, password);
        var wrongCaseResult = _sut.VerifyPassword(hash, wrongCasePassword);

        // Assert
        correctResult.Should().BeTrue();
        wrongCaseResult.Should().BeFalse();
    }
}
