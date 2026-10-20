using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+375446789055")]
    [InlineData("+375456789012")]
    [InlineData("+80447095174")]
    [InlineData("+375291567890")]
    [InlineData("375446789055")] // Should add + automatically
    [InlineData("+375 44 678 90 55")] // Spaces should be removed
    [InlineData("+375-29-123-45-67")] // Dashes should be removed
    [InlineData("+375(44)1234567")] // Parentheses should be removed
    public void Create_WithValidPhoneNumber_ShouldSucceed(string validPhone)
    {
        // Act
        var phone = PhoneNumber.Create(validPhone);

        // Assert
        phone.Should().NotBeNull();
        phone.Value.Should().StartWith("+");
        phone.Value.Should().MatchRegex(@"^\+[1-9]\d{1,14}$");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespace_ShouldThrowDomainException(string? invalidPhone)
    {
        // Act
        Action act = () => PhoneNumber.Create(invalidPhone!);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*Phone number*empty*");
    }

    [Theory]
    [InlineData("+375 29 123-45-6")]
    [InlineData("3752912345678")]
    [InlineData("+123456789012")]
    [InlineData("+3752912A4567")]
    [InlineData("+0123456789")]
    public void Create_WithInvalidFormat_ShouldThrowDomainException(string invalidPhone)
    {
        // Act
        Action act = () => PhoneNumber.Create(invalidPhone);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Invalid Belarusian phone number format*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenEmpty_ShouldThrowDomainException(string emptyPhone)
    {
        // Act
        Action act = () => PhoneNumber.Create(emptyPhone);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Phone number cannot be empty.");
    }

    [Theory]
    [InlineData("80291234567", "+375291234567")]   // 80 prefix
    [InlineData("375447654321", "+375447654321")]  // No plus sign
    [InlineData("259876543", "+375259876543")]     // Subscriber number only (9 digits)
    [InlineData("+375 (33) 111-22-33", "+375331112233")] // With formatting
    public void Create_WithValidFormats_ShouldNormalizeToInternational(string input, string expected)
    {
        // Act
        var phoneNumber = PhoneNumber.Create(input);

        // Assert
        phoneNumber.Value.Should().Be(expected);
    }

    [Fact]
    public void Create_WithSpaces_ShouldRemoveSpaces()
    {
        // Arrange
        const string phoneWithSpaces = "+375 29 123 45 67";

        // Act
        var phone = PhoneNumber.Create(phoneWithSpaces);

        // Assert
        phone.Value.Should().NotContain(" ");
        phone.Value.Should().Be("+375291234567");
    }

    [Fact]
    public void Create_WithDashes_ShouldRemoveDashes()
    {
        // Arrange
        const string phoneWithDashes = "+375-29-123-45-67";

        // Act
        var phone = PhoneNumber.Create(phoneWithDashes);

        // Assert
        phone.Value.Should().NotContain("-");
    }

    [Fact]
    public void Create_WithParentheses_ShouldRemoveParentheses()
    {
        // Arrange
        const string phoneWithParentheses = "+375(29)1234567";

        // Act
        var phone = PhoneNumber.Create(phoneWithParentheses);

        // Assert
        phone.Value.Should().NotContain("(");
        phone.Value.Should().NotContain(")");
    }

    [Fact]
    public void Create_WithoutPlusPrefix_ShouldAddPlus()
    {
        // Arrange
        const string phoneWithoutPlus = "375445678906";

        // Act
        var phone = PhoneNumber.Create(phoneWithoutPlus);

        // Assert
        phone.Value.Should().StartWith("+");
        phone.Value.Should().Be("+375445678906");
    }

    [Fact]
    public void ImplicitConversion_FromPhoneNumber_ShouldReturnValue()
    {
        // Arrange
        var phone = PhoneNumber.Create("+375447095174");

        // Act
        string value = phone;

        // Assert
        value.Should().Be("+375447095174");
    }

    [Fact]
    public void ToString_ShouldReturnValue()
    {
        // Arrange
        var phone = PhoneNumber.Create("+375447095174");

        // Act
        var result = phone.ToString();

        // Assert
        result.Should().Be("+375447095174");
    }

    [Fact]
    public void Equals_WithSamePhoneNumber_ShouldReturnTrue()
    {
        // Arrange
        var phone1 = PhoneNumber.Create("+375446789074");
        var phone2 = PhoneNumber.Create("+375446789074");

        // Act & Assert
        phone1.Equals(phone2).Should().BeTrue();
        (phone1 == phone2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithDifferentPhoneNumber_ShouldReturnFalse()
    {
        // Arrange
        var phone1 = PhoneNumber.Create("+375446789074");
        var phone2 = PhoneNumber.Create("+375654321065");

        // Act & Assert
        phone1.Equals(phone2).Should().BeFalse();
        (phone1 != phone2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_WithSamePhoneNumber_ShouldReturnSameHash()
    {
        // Arrange
        var phone1 = PhoneNumber.Create("+375447095174");
        var phone2 = PhoneNumber.Create("+375447095174");

        // Act & Assert
        phone1.GetHashCode().Should().Be(phone2.GetHashCode());
    }
}