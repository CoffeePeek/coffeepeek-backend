using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;

namespace CoffeePeek.Gateway.Tests;

public sealed class BusinessOwnershipRouteTests
{
    [Fact]
    public void OwnershipRoutesUseResourceAuthorizationAndReachShopsBeforeAccountCatchAll()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "CoffeePeek.Gateway/appsettings.json")));
        var routes = document.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        var owner = routes.GetProperty("shops-owner-route");
        Assert.Equal("Authenticated", owner.GetProperty("AuthorizationPolicy").GetString());
        Assert.Equal("shops-cluster", owner.GetProperty("ClusterId").GetString());

        foreach (var name in new[] { "shops-admin-ownership-claims-route", "shops-admin-ownership-changes-route", "shops-admin-roaster-owner-route" })
        {
            var route = routes.GetProperty(name);
            Assert.Equal("Admin", route.GetProperty("AuthorizationPolicy").GetString());
            Assert.Equal("shops-cluster", route.GetProperty("ClusterId").GetString());
            Assert.True(Precedence(route) < Precedence(routes.GetProperty("account-admin-route")));
        }

        var roasterOwner = routes.GetProperty("shops-admin-roaster-owner-route");
        Assert.Equal("/api/admin/roasters/{id:guid}/owner", roasterOwner.GetProperty("Match").GetProperty("Path").GetString());
        Assert.Equal("PATCH", roasterOwner.GetProperty("Match").GetProperty("Methods")[0].GetString());
        Assert.True(Precedence(roasterOwner) < Precedence(routes.GetProperty("shops-admin-roasters-route")));

        var submit = routes.GetProperty("shops-ownership-claim-submission-route");
        Assert.Equal("shops-cluster", submit.GetProperty("ClusterId").GetString());
        Assert.Equal("Authenticated", submit.GetProperty("AuthorizationPolicy").GetString());
        Assert.Equal("moderation-submission", submit.GetProperty("RateLimiterPolicy").GetString());
        Assert.Equal("/api/owner/ownership-claims", submit.GetProperty("Match").GetProperty("Path").GetString());
        Assert.Equal(new[] { "POST" }, submit.GetProperty("Match").GetProperty("Methods").EnumerateArray().Select(m => m.GetString()));
        Assert.True(Precedence(submit) < Precedence(owner));
        Assert.False(owner.TryGetProperty("RateLimiterPolicy", out _));
    }

    private static decimal Precedence(JsonElement route) => RoutePrecedence.ComputeInbound(
        TemplateParser.Parse(route.GetProperty("Match").GetProperty("Path").GetString()!));
}
