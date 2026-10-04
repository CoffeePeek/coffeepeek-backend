using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.ShopChangeRequests;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Moderation.Application.Services;
using CoffeePeek.ModerationService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Security.Claims;
using Wolverine;

namespace CoffeePeek.ModerationService.Tests;

public class ShopChangeRequestPhotoUrlsTests
{
    private static readonly MediaPublicUrlOptions Options = new()
    {
        PublicEndpoint = "https://media.example.test/",
        ShopBucketName = "coffeepeek.shops"
    };

    [Fact]
    public void Enrich_GalleryPhotosHavePreviewUrlsBeforeApproval()
    {
        var retainedId = Guid.NewGuid();
        var photos = Enumerable.Range(1, 5)
            .Select(i => new UploadedPhotoDto($"photo-{i}.jpg", "image/jpeg", $"shops/photo-{i}.jpg", 42))
            .ToArray();
        var request = Request(ShopChangeSection.Photos, new(Photos: new([retainedId], photos)));

        var result = ShopChangeRequestPhotoUrls.Enrich(request, Options);

        result.Status.Should().Be(ModerationStatus.Pending);
        result.Payload.Photos!.RetainedPhotoIds.Should().Equal(retainedId);
        result.Payload.Photos.NewPhotos.Should().HaveCount(5);
        for (var i = 0; i < photos.Length; i++)
        {
            result.Payload.Photos.NewPhotos[i].Should().BeEquivalentTo(photos[i], x => x.Excluding(p => p.FullUrl));
            result.Payload.Photos.NewPhotos[i].FullUrl.Should().Be($"https://media.example.test/coffeepeek.shops/{photos[i].StorageKey}");
        }
        request.Payload.Photos!.NewPhotos.Should().OnlyContain(photo => photo.FullUrl == null);
    }

    [Fact]
    public void Enrich_MenuPhotosPreserveMenuItemsAndRetainedSelections()
    {
        var item = new UpdateShopMenuItemRequest("espresso", MenuItemAvailability.Present, 5, 30);
        var retainedId = Guid.NewGuid();
        var photo = new UploadedPhotoDto("menu.jpg", "image/jpeg", "menus/menu.jpg", 84);
        var request = Request(ShopChangeSection.Menu, new(Menu: new([item], [retainedId], [photo])));

        var result = ShopChangeRequestPhotoUrls.Enrich(request, Options);

        result.Payload.Menu!.Items.Should().Equal(item);
        result.Payload.Menu.RetainedPhotoIds.Should().Equal(retainedId);
        result.Payload.Menu.NewPhotos.Single().FullUrl.Should().Be("https://media.example.test/coffeepeek.shops/menus/menu.jpg");
        request.Payload.Menu!.NewPhotos.Single().FullUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Enrich_WithoutConfiguredEndpointDoesNotUseClientSuppliedUrl(string endpoint)
    {
        var photo = new UploadedPhotoDto("photo.jpg", "image/jpeg", "shops/photo.jpg", 42, "https://untrusted.example.test/photo.jpg");
        var request = Request(ShopChangeSection.Photos, new(Photos: new([], [photo])));

        var result = ShopChangeRequestPhotoUrls.Enrich(request, new() { PublicEndpoint = endpoint });

        result.Payload.Photos!.NewPhotos.Single().FullUrl.Should().BeNull();
    }

    [Fact]
    public void Enrich_ReplacesClientSuppliedUrlWithConfiguredStorageUrl()
    {
        var photo = new UploadedPhotoDto("photo.jpg", "image/jpeg", "shops/photo.jpg", 42, "https://untrusted.example.test/photo.jpg");
        var request = Request(ShopChangeSection.Photos, new(Photos: new([], [photo])));

        var result = ShopChangeRequestPhotoUrls.Enrich(request, Options);

        result.Payload.Photos!.NewPhotos.Single().FullUrl.Should().Be("https://media.example.test/coffeepeek.shops/shops/photo.jpg");
    }

    [Fact]
    public void Enrich_NonPhotoChangesPreserveAllRequestFields()
    {
        var request = Request(ShopChangeSection.Description, new(Description: "Updated description"));

        var result = ShopChangeRequestPhotoUrls.Enrich(request, Options);

        result.Should().BeEquivalentTo(request);
    }

    [Fact]
    public async Task GetById_ModeratorResponseIncludesPendingPhotoPreview()
    {
        var request = Request(ShopChangeSection.Photos,
            new(Photos: new([], [new("photo.jpg", "image/jpeg", "shops/photo.jpg", 42)])));
        var controller = Controller(Response<ShopChangeRequestDto>.Success(request));

        var result = await controller.GetById(request.Id, TestContext.Current.CancellationToken);

        var response = ((OkObjectResult)result).Value.Should().BeOfType<Response<ShopChangeRequestDto>>().Subject;
        response.Data.Payload.Photos!.NewPhotos.Single().FullUrl.Should().Be("https://media.example.test/coffeepeek.shops/shops/photo.jpg");
        request.Payload.Photos!.NewPhotos.Single().FullUrl.Should().BeNull();
    }

    [Fact]
    public async Task GetAll_EnrichesPreviewWithoutChangingPagination()
    {
        var request = Request(ShopChangeSection.Menu,
            new(Menu: new([], [], [new("menu.jpg", "image/jpeg", "menus/menu.jpg", 42)])));
        var page = new ShopChangeRequestPageDto([request], 4, 4, 2, 1);
        var controller = Controller(Response<ShopChangeRequestPageDto>.Success(page));

        var result = await controller.GetAll(new(), TestContext.Current.CancellationToken);

        var response = ((OkObjectResult)result).Value.Should().BeOfType<Response<ShopChangeRequestPageDto>>().Subject;
        response.Data.TotalItems.Should().Be(4);
        response.Data.CurrentPage.Should().Be(2);
        response.Data.PageSize.Should().Be(1);
        response.Data.Items.Single().Payload.Menu!.NewPhotos.Single().FullUrl.Should().Be("https://media.example.test/coffeepeek.shops/menus/menu.jpg");
        controller.Response.Headers["X-Total-Count"].ToString().Should().Be("4");
    }

    private static ShopChangeRequestsController Controller(object response)
    {
        var bus = DispatchProxy.Create<IMessageBus, ResponseBus>();
        ((ResponseBus)bus).Response = response;
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Admin")
            ], "test"))
        };
        return new(bus, new HeaderUserContext(new HttpContextAccessor { HttpContext = context }),
            new PublicModerationService(null!), Microsoft.Extensions.Options.Options.Create(Options))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    public class ResponseBus : DispatchProxy
    {
        public object Response { get; set; } = null!;

        protected override object Invoke(MethodInfo? targetMethod, object?[]? args) => Response switch
        {
            Response<ShopChangeRequestDto> request => Task.FromResult(request),
            Response<ShopChangeRequestPageDto> page => Task.FromResult(page),
            _ => throw new NotSupportedException()
        };
    }

    private static ShopChangeRequestDto Request(ShopChangeSection section, ShopChangePayloadDto payload) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), section, payload, ModerationStatus.Pending,
        null, null, null, DateTime.UtcNow, null);
}
