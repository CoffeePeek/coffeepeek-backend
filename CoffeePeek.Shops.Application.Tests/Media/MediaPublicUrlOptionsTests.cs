using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Kernel.Options;
using FluentAssertions;

namespace CoffeePeek.Shops.Application.Tests.Media;

public class MediaPublicUrlOptionsTests
{
    private static (bool IsValid, List<ValidationResult> Results) Validate(string imageProxyEndpoint)
    {
        var options = new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.coffeepeek.by",
            ImageProxyEndpoint = imageProxyEndpoint
        };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return (isValid, results);
    }

    [Fact]
    public void ImageProxyEndpoint_DefaultsToEmpty_AndIsValid()
    {
        var options = new MediaPublicUrlOptions { PublicEndpoint = "https://media.coffeepeek.by" };
        var results = new List<ValidationResult>();

        options.ImageProxyEndpoint.Should().BeEmpty();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://media.coffeepeek.by/img")]
    [InlineData("http://localhost:8081")]
    public void ImageProxyEndpoint_EmptyOrHttpUrl_IsValid(string value)
    {
        var (isValid, results) = Validate(value);

        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://x/img")]
    [InlineData("/img")]
    public void ImageProxyEndpoint_NonHttpOrRelative_IsInvalid(string value)
    {
        var (isValid, results) = Validate(value);

        isValid.Should().BeFalse();
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(MediaPublicUrlOptions.ImageProxyEndpoint));
    }
}
