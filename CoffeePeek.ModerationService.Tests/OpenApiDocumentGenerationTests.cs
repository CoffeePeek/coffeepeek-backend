using System.Net;
using System.Text.Json;
using CoffeePeek.ModerationService.Controllers;
using CoffeePeek.Shared.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace CoffeePeek.ModerationService.Tests;

// Regression test for a production 500 on GET /moderation/openapi/v1.json: two [FromQuery]
// nullable-enum action parameters had a non-null literal default (e.g. `ImportQueueStatus? status =
// ImportQueueStatus.Pending`). The C# compiler stores that default in metadata as the raw underlying
// int, and ASP.NET Core's built-in OpenAPI generator crashes trying to unbox it back to the
// Nullable<TEnum> parameter type while building the schema. This boots the real controller
// assembly through the real OpenAPI document pipeline (no DB/Wolverine needed) to catch any
// controller parameter that reintroduces the pattern.
public class OpenApiDocumentGenerationTests
{
    [Fact]
    public async Task OpenApiDocument_Generates_Without_Throwing()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(AdminImportController).Assembly);
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityTransformer>());
        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        await using var app = builder.Build();
        app.MapControllers();
        app.MapOpenApi();

        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();

            using var client = new HttpClient();
            var response = await client.GetAsync($"{address}/openapi/v1.json");
            var body = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.OK, "the OpenAPI document must generate successfully; body: {0}", body);

            var document = JsonDocument.Parse(body);
            document.RootElement.TryGetProperty("paths", out _).Should().BeTrue();
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
