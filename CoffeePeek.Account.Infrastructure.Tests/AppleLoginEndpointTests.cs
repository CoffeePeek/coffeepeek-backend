using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Wolverine;

namespace CoffeePeek.Account.Infrastructure.Tests;

public sealed class AppleLoginEndpointTests
{
    [Fact]
    public void CommandLogging_DoesNotExposeIdentityToken()
    {
        var command = new AppleLoginCommand(Guid.NewGuid(), "secret-id-token");
        command.ToString().Should().NotContain("secret-id-token");
    }

    [Fact]
    public async Task Login_ValidBody_ReachesHandlerAndReturnsItsStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var challengeId = Guid.NewGuid();
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.InvokeAsync<Response<AppleLoginResponse>>(
                It.Is<object>(m => m.GetType() == typeof(AppleLoginCommand) &&
                                   ((AppleLoginCommand)m).ChallengeId == challengeId &&
                                   ((AppleLoginCommand)m).IdToken == "test-token"),
                It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(Response<AppleLoginResponse>.Error(HttpStatusCode.Unauthorized, "Invalid Apple token"));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(AppleTokensController).Assembly.FullName
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(Mock.Of<IAppleLoginChallengeStore>());
        builder.Services.AddSingleton(bus.Object);
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(AppleTokensController).Assembly);
        await using var app = builder.Build();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync(ct);

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var response = await client.PostAsJsonAsync("/api/Tokens/apple/login",
            new { challengeId, idToken = "test-token" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        bus.VerifyAll();

        using var missingToken = await client.PostAsJsonAsync("/api/Tokens/apple/login", new { challengeId }, ct);
        missingToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await app.StopAsync(ct);
    }
}
