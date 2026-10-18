using CoffeePeek.Shared.Web.Extensions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Gateway.Tests;

public sealed class PhotoVariantCorsTests
{
    [Fact]
    public void BrowserCanReadValidatorAndAgeAndSendConditionalHeadersOnAllowedOrigins()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        builder.Services.AddCorsModule(builder.Environment);
        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = options.GetPolicy(options.DefaultPolicyName)!;
        Assert.Contains("ETag", policy.ExposedHeaders);
        Assert.Contains("Date", policy.ExposedHeaders);
        Assert.Contains("Age", policy.ExposedHeaders);
        Assert.True(policy.AllowAnyHeader);
        Assert.True(policy.AllowAnyMethod);
        Assert.True(policy.IsOriginAllowed("http://localhost:3000"));
        Assert.False(policy.IsOriginAllowed("https://untrusted.example"));
    }
}
