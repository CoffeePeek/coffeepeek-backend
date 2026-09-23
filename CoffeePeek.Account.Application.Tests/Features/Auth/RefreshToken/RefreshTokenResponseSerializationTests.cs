using System;
using System.Text.Json;
using CoffeePeek.Account.Application.Features.Auth.RefreshToken;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Auth.RefreshToken;

public class RefreshTokenResponseSerializationTests
{
    [Fact]
    public void BrowserResponse_DoesNotSerializeRefreshToken()
    {
        var response = Response<RefreshTokenResponse>.Success(new RefreshTokenResponse(
            "new-access",
            "secret-refresh",
            DateTime.UtcNow.AddMinutes(15)));

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("new-access");
        json.Should().Contain("accessTokenExpiresAt");
        json.Should().NotContain("secret-refresh");
        json.Should().NotContain("refreshToken");
    }
}
