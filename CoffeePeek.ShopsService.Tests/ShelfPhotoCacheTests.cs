using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Infrastructure.Media;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public class ShelfPhotoCacheTests
{
    [Fact]
    public async Task OwnerGetsPrivateCardAndConditionalReadStillChecksOwnership()
    {
        var owner = Guid.NewGuid();
        var bag = Guid.NewGuid();
        var card = Guid.NewGuid();
        var access = new ShelfBagPhotoAccess(owner, bag, card, "image/jpeg");
        var queries = new Mock<IShelfQueries>();
        queries.Setup(q => q.GetPhotoAsync(owner, bag, card, It.IsAny<CancellationToken>()))
            .ReturnsAsync(access);
        var media = new CardHandler();
        var controller = CreateController(owner, queries, media);

        Assert.IsType<EmptyResult>(await controller.Photo(bag, card, TestContext.Current.CancellationToken));
        Assert.Equal(1, media.Calls);
        Assert.Equal($"\"shelf-{card:N}\"", controller.Response.Headers.ETag);
        Assert.StartsWith("private, max-age=", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("Authorization, Cookie", controller.Response.Headers.Vary);
        Assert.Equal(3, controller.Response.Body.Length);

        var etag = $"\"shelf-{card:N}\"";
        foreach (var condition in new[] { etag, $"W/{etag}", $"\"other\", {etag}",
                     $"\"other,inside\", W/{etag}", "*" })
        {
            var conditional = CreateController(owner, queries, media);
            conditional.Request.Headers.IfNoneMatch = condition;
            Assert.Equal(304, Assert.IsType<StatusCodeResult>(await conditional.Photo(bag, card,
                TestContext.Current.CancellationToken)).StatusCode);
        }
        Assert.Equal(1, media.Calls);

        queries.Setup(q => q.GetPhotoAsync(owner, bag, card, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShelfBagPhotoAccess?)null);
        var deleted = CreateController(owner, queries, media);
        deleted.Request.Headers.IfNoneMatch = $"\"shelf-{card:N}\"";
        Assert.IsType<NotFoundResult>(await deleted.Photo(bag, card, TestContext.Current.CancellationToken));
        Assert.Equal("private, no-store", deleted.Response.Headers.CacheControl);
        Assert.Equal(1, media.Calls);
    }

    private static ShelfController CreateController(Guid owner, Mock<IShelfQueries> queries, CardHandler media)
    {
        var user = new Mock<IUserContext>();
        user.Setup(u => u.GetUserIdOrThrow()).Returns(owner);
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(new HttpClient(media)
        {
            BaseAddress = new Uri("http://media.test"),
        });
        var client = new MediaShelfBagPhotoClient(clients.Object, Options.Create(new GatewayAuthOptions()),
            NullLogger<MediaShelfBagPhotoClient>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AccessTokenExpiry.ClaimType, DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString())], "test"));
        return new ShelfController(Mock.Of<IMessageBus>(), user.Object, queries.Object, client)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private sealed class CardHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var bytes = new ByteArrayContent([1, 2, 3]);
            bytes.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = bytes });
        }
    }
}
