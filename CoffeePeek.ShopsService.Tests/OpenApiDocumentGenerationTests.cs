using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Web;
using CoffeePeek.Shops.Application.Features.Review.Reports;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.ShopsService.Controllers;
using FluentAssertions;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public class OpenApiDocumentGenerationTests
{
    [Fact]
    public async Task OpenApiDocument_Generates_AndPreservesReviewReportStatusBinding()
    {
        var ct = TestContext.Current.CancellationToken;
        var bus = DispatchProxy.Create<IMessageBus, ReviewReportApiContractTests.RecordingBus>();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(AdminReviewReportsController).Assembly);
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityTransformer>());
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy => policy.RequireRole("Admin")));
        builder.Services.AddSingleton(bus);
        builder.Services.AddSingleton(Mock.Of<IUserContext>());
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        await using var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.Use((context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test"));
            return next(context);
        });
        app.UseAuthorization();
        app.MapControllers();
        app.MapOpenApi();
        await app.StartAsync(ct);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            foreach (var (query, expected) in new[]
                     {
                         ("", ReviewReportStatus.Pending),
                         ("?status=Pending", ReviewReportStatus.Pending),
                         ("?status=Dismissed", ReviewReportStatus.Dismissed),
                         ("?status=ReviewDeleted", ReviewReportStatus.ReviewDeleted)
                     })
            {
                using var response = await client.GetAsync($"/api/admin/review-reports{query}", ct);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                ((ReviewReportApiContractTests.RecordingBus)(object)bus).LastMessage
                    .Should().Be(new GetReviewReportsQuery(expected, 1, 20));
            }

            using var documentResponse = await client.GetAsync("/openapi/v1.json", ct);
            var body = await documentResponse.Content.ReadAsStringAsync(ct);
            documentResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                "the real Shops OpenAPI document must generate successfully; body: {0}", body);
            using var document = JsonDocument.Parse(body);
            var paths = document.RootElement.GetProperty("paths");
            paths.TryGetProperty("/api/admin/review-reports", out _).Should().BeTrue();
            foreach (var route in new[] { "/api/Catalogs/roasters", "/api/v1/roasters/search", "/api/v1/roasters/facets" })
                paths.TryGetProperty(route, out _).Should().BeFalse();
            var parameters = paths.GetProperty("/api/roasters").GetProperty("get").GetProperty("parameters");
            parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString()).Should().Equal("tags");
            body.Should().NotContain("coffeeCatalogUpdatedAtUtc");
        }
        finally
        {
            await app.StopAsync(ct);
        }
    }
}
