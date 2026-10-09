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
    public async Task OpenApiDocument_GeneratesCanonicalCheckInsAndPreservesReportStatusBinding()
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
                         ("?status=CheckInDeleted", ReviewReportStatus.CheckInDeleted)
                     })
            {
                using var response = await client.GetAsync($"/api/admin/check-in-reports{query}", ct);
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
            var timeline = paths.GetProperty("/api/v1/feed").GetProperty("get");
            timeline.GetProperty("parameters").EnumerateArray().Select(x => x.GetProperty("name").GetString())
                .Should().Contain(new[] { "citySlug", "coffeeShopSlug", "authorSlug", "pageSize", "cursor" });
            var timelineEnvelope = ResolveSchema(document.RootElement,
                timeline.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
            var timelinePage = SchemaProperties(document.RootElement, SchemaProperties(document.RootElement, timelineEnvelope)["data"]);
            timelinePage.Keys.Should().Contain(new[] { "items", "nextCursor" });
            var timelineItems = ResolveSchema(document.RootElement, timelinePage["items"]);
            SchemaProperties(document.RootElement, timelineItems.GetProperty("items")).Keys
                .Should().Contain(new[] { "publishedAtUtc", "checkIn" });
            paths.TryGetProperty("/api/admin/check-in-reports", out _).Should().BeTrue();
            foreach (var route in new[] { "/api/Catalogs/roasters", "/api/v1/roasters/search", "/api/v1/roasters/facets" })
                paths.TryGetProperty(route, out _).Should().BeFalse();
            var parameters = paths.GetProperty("/api/roasters").GetProperty("get").GetProperty("parameters");
            parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString()).Should().Equal("tags");
            body.Should().NotContain("coffeeCatalogUpdatedAtUtc");

            var checkIns = paths.GetProperty("/api/v1/check-ins");
            checkIns.TryGetProperty("post", out var create).Should().BeTrue();
            checkIns.TryGetProperty("get", out var feed).Should().BeTrue();
            paths.GetProperty("/api/v1/check-ins/mine").TryGetProperty("get", out _).Should().BeTrue();
            paths.GetProperty("/api/v1/check-ins/{id}").TryGetProperty("get", out _).Should().BeTrue();
            var request = ResolveSchema(document.RootElement,
                create.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
            request.GetProperty("required").EnumerateArray().Select(x => x.GetString())
                .Should().BeEquivalentTo(new[] { "coffeeShopSlug", "visitedAt" });
            var requestProperties = request.GetProperty("properties");
            requestProperties.TryGetProperty("drinkSlug", out _).Should().BeTrue();
            requestProperties.TryGetProperty("customDrinkName", out _).Should().BeTrue();
            foreach (var identity in new[] { "coffeeShopId", "userId", "username", "drinkDefinitionId" })
                requestProperties.TryGetProperty(identity, out _).Should().BeFalse();

            var envelope = ResolveSchema(document.RootElement,
                create.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
            var publicDto = SchemaProperties(document.RootElement, envelope)["data"];
            var dtoProperties = SchemaProperties(document.RootElement, publicDto);
            foreach (var field in new[] { "id", "shop", "author", "drinkSlug", "customDrinkName" })
                dtoProperties.TryGetValue(field, out _).Should().BeTrue();
            foreach (var identity in new[] { "shopId", "userId", "drinkDefinitionId", "drink" })
                dtoProperties.TryGetValue(identity, out _).Should().BeFalse();
            var filters = feed.GetProperty("parameters").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToArray();
            filters.Should().Contain(new[] { "citySlug", "coffeeShopSlug", "authorSlug" });
            filters.Should().NotContain(new[] { "cityId", "coffeeShopId", "userId" });
            foreach (var path in paths.EnumerateObject().Select(x => x.Name))
            {
                path.Should().NotContain("CoffeeShopReviews").And.NotContain("/api/CheckIns")
                    .And.NotContain("/api/v2/check-ins").And.NotContain("/api/admin/review-reports");
            }
        }
        finally
        {
            await app.StopAsync(ct);
        }
    }

    private static Dictionary<string, JsonElement> SchemaProperties(JsonElement document, JsonElement schema)
    {
        schema = ResolveSchema(document, schema);
        var properties = new Dictionary<string, JsonElement>();
        if (schema.TryGetProperty("properties", out var direct))
            foreach (var property in direct.EnumerateObject()) properties[property.Name] = property.Value;
        foreach (var composition in new[] { "allOf", "anyOf", "oneOf" })
            if (schema.TryGetProperty(composition, out var parts))
                foreach (var part in parts.EnumerateArray())
                    foreach (var property in SchemaProperties(document, part)) properties[property.Key] = property.Value;
        return properties;
    }

    private static JsonElement ResolveSchema(JsonElement document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference)) return schema;
        var current = document;
        foreach (var segment in reference.GetString()![2..].Split('/'))
            current = current.GetProperty(segment.Replace("~1", "/").Replace("~0", "~"));
        return ResolveSchema(document, current);
    }
}
