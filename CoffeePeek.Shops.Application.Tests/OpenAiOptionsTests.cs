using CoffeePeek.Shared.Kernel.Options;
using FluentAssertions;

namespace CoffeePeek.Shops.Application.Tests;

public class OpenAiOptionsTests
{
    [Fact]
    public void DefaultTimeout_IsLongEnoughForVisionParse()
    {
        new OpenAiOptions().TimeoutSeconds.Should().BeGreaterThanOrEqualTo(90);
    }

    [Fact]
    public void DefaultModel_IsOpenAiVisionModel()
    {
        new OpenAiOptions().Model.Should().Be("gpt-4.1-mini");
        new OpenAiOptions().BaseUrl.Should().Be("https://api.openai.com/v1");
    }
}
