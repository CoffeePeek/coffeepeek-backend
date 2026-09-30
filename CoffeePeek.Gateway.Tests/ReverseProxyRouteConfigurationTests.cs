using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;

namespace CoffeePeek.Gateway.Tests;

public class ReverseProxyRouteConfigurationTests
{
    [Fact]
    public void AppReleaseAutomationRoute_IsLimitedToReleaseListAndCreate()
    {
        using var document = LoadGatewayAppsettings();
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        routes.TryGetProperty("shops-admin-v1-app-downloads-automation-route", out var route)
            .Should().BeTrue();
        route.GetProperty("ClusterId").GetString().Should().Be("shops-cluster");
        route.GetProperty("AuthorizationPolicy").GetString().Should().Be("AppReleaseAutomationPolicy");
        route.GetProperty("Match").GetProperty("Path").GetString()
            .Should().Be("/api/admin/v1/app-downloads/android/releases");
        route.GetProperty("Match").GetProperty("Methods").EnumerateArray()
            .Select(method => method.GetString())
            .Should().Equal("GET", "POST");
    }

    [Theory]
    [InlineData("shops-admin-cities-route", "/api/admin/cities/{**remainder}")]
    [InlineData("shops-admin-equipments-route", "/api/admin/equipments/{**remainder}")]
    [InlineData("shops-admin-beans-route", "/api/admin/beans/{**remainder}")]
    [InlineData("shops-admin-roasters-route", "/api/admin/roasters/{**remainder}")]
    [InlineData("shops-admin-brew-methods-route", "/api/admin/brew-methods/{**remainder}")]
    [InlineData("shops-admin-coffee-zones-route", "/api/admin/coffee-zones/{**remainder}")]
    public void AdminCatalogCrudRoutes_TargetShopsService(string routeId, string path)
    {
        using var document = LoadGatewayAppsettings();
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        routes.TryGetProperty(routeId, out var route).Should().BeTrue();
        route.GetProperty("ClusterId").GetString().Should().Be("shops-cluster");
        route.GetProperty("AuthorizationPolicy").GetString().Should().Be("Moderator");
        route.GetProperty("Match").GetProperty("Path").GetString().Should().Be(path);
    }

    [Fact]
    public void AccountAdminCatchAll_RemainsAfterShopsAdminCatalogRoutes()
    {
        using var document = LoadGatewayAppsettings();
        var routeIds = document.RootElement
            .GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .EnumerateObject()
            .Select(route => route.Name)
            .ToList();

        var accountAdminIndex = routeIds.IndexOf("account-admin-route");
        accountAdminIndex.Should().BeGreaterThan(-1);

        foreach (var shopsCatalogRoute in new[]
        {
            "shops-admin-cities-route",
            "shops-admin-equipments-route",
            "shops-admin-beans-route",
            "shops-admin-roasters-route",
            "shops-admin-brew-methods-route",
            "shops-admin-coffee-zones-route"
        })
        {
            routeIds.IndexOf(shopsCatalogRoute).Should().BeLessThan(accountAdminIndex);
        }
    }

    [Theory]
    [InlineData("shops-admin-stats-route", "shops-cluster", "/api/admin/stats/shops/{**remainder}")]
    [InlineData("moderation-admin-stats-route", "moderation-cluster", "/api/admin/stats/moderation/{**remainder}")]
    public void AdminStatsRoutes_TargetOwningServiceWithAdminPolicyBeforeAccountCatchAll(
        string routeId,
        string clusterId,
        string path)
    {
        using var document = LoadGatewayAppsettings();
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        routes.TryGetProperty(routeId, out var route).Should().BeTrue();
        route.GetProperty("ClusterId").GetString().Should().Be(clusterId);
        route.GetProperty("AuthorizationPolicy").GetString().Should().Be("Admin");
        route.GetProperty("Match").GetProperty("Path").GetString().Should().Be(path);

        var routeIds = routes.EnumerateObject().Select(r => r.Name).ToList();
        routeIds.IndexOf(routeId).Should().BeLessThan(routeIds.IndexOf("account-admin-route"));
    }

    [Fact]
    public void ModerationRoastersRoute_TargetsModerationClusterWithSubmissionRateLimit()
    {
        using var document = LoadGatewayAppsettings();
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        routes.TryGetProperty("moderation-ModerationRoasters-route", out var route).Should().BeTrue();
        route.GetProperty("ClusterId").GetString().Should().Be("moderation-cluster");
        route.GetProperty("RateLimiterPolicy").GetString().Should().Be("moderation-submission");
        route.GetProperty("Match").GetProperty("Path").GetString().Should().Be("/api/ModerationRoasters/{**remainder}");
    }

    [Fact]
    public void PublicRoastersRoute_TargetsShopsCluster()
    {
        using var document = LoadGatewayAppsettings();
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        routes.TryGetProperty("shops-Roasters-route", out var route).Should().BeTrue();
        route.GetProperty("ClusterId").GetString().Should().Be("shops-cluster");
        route.GetProperty("Match").GetProperty("Path").GetString().Should().Be("/api/roasters/{**remainder}");
    }

    [Theory]
    [InlineData("account-deletion-confirm-route", "/api/Users/me/deletion-confirmation", "POST", false)]
    [InlineData("account-deletion-status-route", "/api/Users/me/deletion-request", "GET", true)]
    [InlineData("account-deletion-cancel-route", "/api/Users/me/deletion-request", "DELETE", false)]
    [InlineData("account-deletion-request-route", "/api/Users/me", "DELETE", true)]
    public void AccountDeletionRoutes_AreMethodSpecificRateLimitedAndPreserveAuth(
        string routeId,
        string path,
        string method,
        bool requiresAuthentication)
    {
        using var document = LoadGatewayAppsettings();
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        var route = routes.GetProperty(routeId);

        route.GetProperty("ClusterId").GetString().Should().Be("account-cluster");
        route.GetProperty("RateLimiterPolicy").GetString().Should().Be("account-deletion");
        route.GetProperty("Match").GetProperty("Path").GetString().Should().Be(path);
        route.GetProperty("Match").GetProperty("Methods").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal(method);

        route.TryGetProperty("AuthorizationPolicy", out var authorizationPolicy)
            .Should().Be(requiresAuthentication);
        if (requiresAuthentication)
            authorizationPolicy.GetString().Should().Be("Authenticated");
    }

    [Fact]
    public void AccountDeletionRoutes_PrecedeUsersCatchAll()
    {
        using var document = LoadGatewayAppsettings();
        var routeIds = document.RootElement
            .GetProperty("ReverseProxy")
            .GetProperty("Routes")
            .EnumerateObject()
            .Select(route => route.Name)
            .ToList();
        var usersCatchAllIndex = routeIds.IndexOf("account-Users-route");

        routeIds.IndexOf("account-deletion-confirm-route").Should().BeLessThan(usersCatchAllIndex);
        routeIds.IndexOf("account-deletion-status-route").Should().BeLessThan(usersCatchAllIndex);
        routeIds.IndexOf("account-deletion-cancel-route").Should().BeLessThan(usersCatchAllIndex);
        routeIds.IndexOf("account-deletion-request-route").Should().BeLessThan(usersCatchAllIndex);
    }

    [Theory]
    [InlineData("account-Users-by-slug-route", "/api/Users/by-slug/{slug}")]
    [InlineData("account-Users-public-addresses-route", "/api/Users/public-addresses")]
    [InlineData("account-Users-public-address-route", "/api/Users/{id:guid}/public-address")]
    public void PublicUserAddressRoutes_TargetAccountWithoutConsumingAuthRateLimit(string routeId, string path)
    {
        using var document = LoadGatewayAppsettings();
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        routes.TryGetProperty(routeId, out var route).Should().BeTrue();
        route.GetProperty("ClusterId").GetString().Should().Be("account-cluster");
        route.GetProperty("Match").GetProperty("Path").GetString().Should().Be(path);
        route.GetProperty("RateLimiterPolicy").GetString().Should().Be("global");
        routes.GetProperty("account-Users-route").GetProperty("RateLimiterPolicy").GetString().Should().Be("auth-endpoints");

        // D-07: anonymous read, same as the existing GUID profile lookup on the Users catch-all.
        route.TryGetProperty("AuthorizationPolicy", out _).Should().BeFalse();

        var routeIds = routes.EnumerateObject().Select(r => r.Name).ToList();
        var usersCatchAllIndex = routeIds.IndexOf("account-Users-route");
        usersCatchAllIndex.Should().BeGreaterThan(-1);
        routeIds.IndexOf(routeId).Should().BeLessThan(usersCatchAllIndex);
    }

    [Fact]
    public void UserBySlugRoute_OutranksShopsUserReviewsForEverySlug()
    {
        using var document = LoadGatewayAppsettings();
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");

        routes.TryGetProperty("account-Users-by-slug-route", out _).Should().BeTrue();
        var bySlug = ParseRouteTemplate(routes, "account-Users-by-slug-route");
        var shopsReviews = ParseRouteTemplate(routes, "shops-users-reviews-route");
        var usersCatchAll = ParseRouteTemplate(routes, "account-Users-route");

        // Non-vacuous: the "reviews" slug really is matched by all three templates (Pitfall 4).
        const string reviewsSlugPath = "/api/Users/by-slug/reviews";
        Matches(bySlug, reviewsSlugPath).Should().BeTrue();
        Matches(shopsReviews, reviewsSlugPath).Should().BeTrue();
        Matches(usersCatchAll, reviewsSlugPath).Should().BeTrue();

        // Lower inbound precedence is more specific, so the by-slug route wins for every slug.
        RoutePrecedence.ComputeInbound(bySlug).Should().BeLessThan(RoutePrecedence.ComputeInbound(shopsReviews));
        RoutePrecedence.ComputeInbound(bySlug).Should().BeLessThan(RoutePrecedence.ComputeInbound(usersCatchAll));

        Matches(bySlug, "/api/Users/by-slug/ivan-petrov").Should().BeTrue();

        // Real Shops user-review paths are not captured by the by-slug route.
        const string shopsUserReviewsPath = "/api/users/3fa85f64-5717-4562-b3fc-2c963f66afa6/reviews";
        Matches(bySlug, shopsUserReviewsPath).Should().BeFalse();
        Matches(shopsReviews, shopsUserReviewsPath).Should().BeTrue();
    }

    private static RouteTemplate ParseRouteTemplate(JsonElement routes, string routeId)
    {
        var path = routes.GetProperty(routeId).GetProperty("Match").GetProperty("Path").GetString();
        path.Should().NotBeNullOrWhiteSpace();
        return TemplateParser.Parse(path!.TrimStart('/'));
    }

    private static bool Matches(RouteTemplate template, string path) =>
        new TemplateMatcher(template, new RouteValueDictionary())
            .TryMatch(new PathString(path), new RouteValueDictionary());

    private static JsonDocument LoadGatewayAppsettings()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "CoffeePeek.Gateway",
            "appsettings.json"));

        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
