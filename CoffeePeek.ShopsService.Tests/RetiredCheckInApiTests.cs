using System.Reflection;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.ShopsService.Tests;

public class RetiredCheckInApiTests
{
    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void OnlyCanonicalControllersAndProtectedPhotoRoutesRemain()
    {
        foreach (var type in new[] { typeof(UnifiedCheckInsController), typeof(CheckInPhotosController) })
        {
            Assert.Equal("api/v1/check-ins", type.GetCustomAttribute<RouteAttribute>()!.Template);
            Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        }
        var controllerNames = typeof(UnifiedCheckInsController).Assembly.GetTypes().Select(type => type.Name).ToArray();
        foreach (var retired in new[] { "CheckInsController", "CoffeeShopReviewsController", "UserReviewsController", "InternalCheckInsController" })
            Assert.DoesNotContain(retired, controllerNames);

        var moderatorPhoto = typeof(CheckInPhotosController).GetMethod(nameof(CheckInPhotosController.DownloadModeration))!;
        Assert.Equal("{id:guid}/moderation-photos/{photoId:guid}", moderatorPhoto.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(RoleConsts.Moderator, moderatorPhoto.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("{id:guid}/photos/{photoId:guid}", typeof(CheckInPhotosController)
            .GetMethod(nameof(CheckInPhotosController.Download))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void ContractsAndAggregateHaveNoCompatibilityFieldsOrLegacyFactory()
    {
        var assembly = typeof(UnifiedCheckInDto).Assembly;
        foreach (var retired in new[]
        {
            "CoffeePeek.Contract.Dtos.CoffeeShop.ReviewDto",
            "CoffeePeek.Contract.Dtos.CoffeeShop.CheckInDto",
            "CoffeePeek.Contract.Dtos.CheckIns.LegacyModerationSnapshotDto",
            "CoffeePeek.Contract.Events.Shops.ReviewAddedEvent",
            "CoffeePeek.Contract.Events.Moderation.ModerationReviewApprovedEvent",
            "CoffeePeek.Contract.Events.Moderation.ModerationReviewEditedEvent",
            "CoffeePeek.Contract.Events.Moderation.ModerationReviewLinkedEvent"
        }) Assert.Null(assembly.GetType(retired));

        foreach (var field in new[] { "LegacyIncomplete", "LegacyHeader", "ReviewId" })
        {
            Assert.Null(typeof(UnifiedCheckInDto).GetProperty(field));
            Assert.Null(typeof(DomainCheckIn).GetProperty(field));
        }
        foreach (var method in new[] { "Create", "ImportHistorical", "UpdateNote", "AssignRating", "PreserveLegacyHeader" })
            Assert.Null(typeof(DomainCheckIn).GetMethod(method, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance));

        Assert.Null(DomainCheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author",
            null, null, DateTime.UtcNow).Rating);
        Assert.Equal(typeof(Guid), typeof(ModerationReviewDto).GetProperty(nameof(ModerationReviewDto.CheckInId))!.PropertyType);
        Assert.Equal(typeof(int), typeof(ModerationReviewDto).GetProperty(nameof(ModerationReviewDto.ContentRevision))!.PropertyType);
        Assert.Equal(typeof(DateTime), typeof(ModerationReviewDto).GetProperty(nameof(ModerationReviewDto.VisitedAtUtc))!.PropertyType);
        Assert.Null(typeof(ModerationReviewDto).GetProperty("Header"));
        Assert.Null(typeof(ModerationReviewDto).GetProperty("Comment"));
    }

    [Fact]
    public void ShopContractsExposeCanonicalVisitsAndOneCheckInCount()
    {
        foreach (var type in new[] { typeof(ShopDto), typeof(CoffeeShopDetailsDto), typeof(ShortShopDto) })
        {
            Assert.Equal(typeof(int), type.GetProperty("CheckInCount")!.PropertyType);
            Assert.Null(type.GetProperty("ReviewCount"));
            Assert.Null(type.GetProperty("Reviews"));
            Assert.Null(type.GetProperty("UserReviews"));
        }
        Assert.Equal(typeof(UnifiedCheckInDto[]), typeof(ShopDto).GetProperty("CheckIns")!.PropertyType);
        Assert.Equal(typeof(UnifiedCheckInDto[]), typeof(CoffeeShopDetailsDto).GetProperty("CheckIns")!.PropertyType);
        Assert.Equal(typeof(UnifiedCheckInDto[]), typeof(CoffeeShopDetailsDto).GetProperty("UserCheckIns")!.PropertyType);
    }

    [Fact]
    public void MappedPhotoUrlsAndSnapshotIdentityUseTheCanonicalVisit()
    {
        var visit = DomainCheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", "Good coffee",
            Rating.Create(4, 3, 5), DateTime.UtcNow);
        var photo = new ShopPhoto("coffee.jpg", "image/jpeg", "private-object", 42, visit.UserId);
        visit.AddPhotos([photo]);

        var dto = CheckInMapping.ToDto(visit);
        Assert.Equal($"/api/v1/check-ins/{visit.Id}/photos/{photo.Id}", Assert.Single(dto.Photos).Url);
        Assert.Equal(visit.Note, dto.Text);
        Assert.NotNull(dto.Rating);
        Assert.Equal(5, dto.Rating.Coffee);
        var snapshot = CheckInMapping.ModerationRequest(visit);
        Assert.Equal(visit.Id, snapshot.CheckInId);
        Assert.Equal(visit.ContentRevision, snapshot.ContentRevision);
        Assert.Equal(dto.Photos[0].Url, Assert.Single(snapshot.Photos).Url);
    }

    [Fact]
    public void GatewayAndModeratorSourceExposeOnlySupportedRoutes()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "CoffeePeek.Gateway/appsettings.json")));
        var routes = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes").EnumerateObject()
            .Select(route => route.Value).ToArray();
        Assert.Equal("shops-cluster", Assert.Single(routes, route => route.GetProperty("Match")
            .GetProperty("Path").GetString() == "/api/v1/check-ins/{**remainder}").GetProperty("ClusterId").GetString());
        Assert.Equal("moderation-cluster", Assert.Single(routes, route => route.GetProperty("Match")
            .GetProperty("Path").GetString() == "/api/v1/moderation/check-ins/{**remainder}").GetProperty("ClusterId").GetString());
        foreach (var route in routes)
        {
            var path = route.GetProperty("Match").GetProperty("Path").GetString()!;
            Assert.False(path.Contains("/api/v2/check-ins", StringComparison.OrdinalIgnoreCase));
            Assert.False(path.Contains("/api/CheckIns", StringComparison.OrdinalIgnoreCase));
            Assert.False(path.Contains("CoffeeShopReviews", StringComparison.OrdinalIgnoreCase));
            Assert.False(path.Contains("ModerationReviews", StringComparison.OrdinalIgnoreCase));
            Assert.False(path.EndsWith("/reviews", StringComparison.OrdinalIgnoreCase));
        }

        var moderator = File.ReadAllText(Path.Combine(RepositoryRoot, "CoffeePeek.ModerationService/Controllers/ModerationReviewsController.cs"));
        Assert.Contains("[Route(\"api/v1/moderation/check-ins\")]", moderator);
        Assert.Contains("[Authorize(Policy = RoleConsts.Moderator)]", moderator);
        Assert.DoesNotContain("[HttpPost", moderator);
        Assert.DoesNotContain("[HttpGet(\"mine", moderator);
        var photoMapper = File.ReadAllText(Path.Combine(RepositoryRoot, "CoffeePeek.Moderation.Application/Mapper/MapsterConfiguration.ModerationReview.cs"));
        Assert.Contains("/api/v1/check-ins/{review.CheckInId}/moderation-photos/", photoMapper);
        Assert.DoesNotContain("BuildPublicUrl", photoMapper);
    }
}
