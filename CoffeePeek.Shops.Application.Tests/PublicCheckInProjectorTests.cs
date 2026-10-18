#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.PublicAddresses;
using Moq;
using Xunit;

namespace CoffeePeek.Shops.Application.Tests;

public class PublicCheckInProjectorTests
{
    [Fact]
    public async Task PublicPhotoAndDrinkCutoutKeepLegacyUrlsAndReadyVariants()
    {
        var ct = TestContext.Current.CancellationToken;
        var addresses = new Mock<IPublicAddressService>();
        addresses.Setup(a => a.GetForReadAsync(It.IsAny<PublicAddressKind>(), It.IsAny<Guid[]>(), ct)).ReturnsAsync([]);
        var profiles = new Mock<IProfileAddressLookup>();
        profiles.Setup(p => p.GetProfilesByIdsAsync(It.IsAny<Guid[]>(), ct)).ReturnsAsync([]);
        var photo = new CheckInPhotoDto(Guid.NewGuid(), "photo.jpg", "image/jpeg", "private/source", 999, 0, "/protected/original")
        {
            Variants = [new CheckInPhotoVariantDto("feed", "webp", "image/webp", 100, 200, 123,
                new string('a', 64), 1, "/protected/variant")]
        };
        var item = new UnifiedCheckInDto { Id = Guid.NewGuid(), Photos = [photo], DrinkPhoto = photo };
        var projector = new PublicCheckInProjector(addresses.Object, profiles.Object);
        var result = (await projector.ProjectAsync([item], ct)).Single();
        Assert.Equal(photo.Url, result.Photos.Single().Url); Assert.Equal(photo.Url, result.DrinkPhotoUrl);
        Assert.Same(photo.Variants, result.Photos.Single().Variants); Assert.Same(photo.Variants, result.DrinkPhotoVariants);
        var legacy = (await projector.ProjectAsync([item with { Photos = [photo with { Variants = null }], DrinkPhoto = photo with { Variants = null } }], ct)).Single();
        var json = System.Text.Json.JsonSerializer.Serialize(legacy, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.DoesNotContain("variants", json); Assert.DoesNotContain("drinkPhotoVariants", json);
        Assert.Contains("drinkPhotoUrl", json);
    }

    [Fact]
    public async Task BatchesDistinctAuthorsAndKeepsVisitsWhoseProfileOrAvatarIsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        var shopId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var withoutAvatarId = Guid.NewGuid();
        var missingProfileId = Guid.NewGuid();
        var visits = new[] { authorId, authorId, withoutAvatarId, missingProfileId, Guid.Empty }
            .Select(userId => new UnifiedCheckInDto { Id = Guid.NewGuid(), ShopId = shopId, UserId = userId })
            .ToArray();
        const string avatarUrl = "https://media.example.com/coffeepeek.avatars/example-author.jpg";
        var addresses = new Mock<IPublicAddressService>(MockBehavior.Strict);
        addresses.Setup(x => x.GetForReadAsync(PublicAddressKind.CoffeeShop,
                It.Is<Guid[]>(ids => ids.SequenceEqual(new[] { shopId })), ct))
            .ReturnsAsync([new(shopId, "example-coffee", "/coffee-shops/example-coffee", 1)]);
        var profiles = new Mock<IProfileAddressLookup>(MockBehavior.Strict);
        profiles.Setup(x => x.GetProfilesByIdsAsync(
                It.Is<Guid[]>(ids => ids.SequenceEqual(new[] { authorId, withoutAvatarId, missingProfileId })), ct))
            .ReturnsAsync([new(new(authorId, "example-author", "/users/example-author", 1), avatarUrl),
                new(new(withoutAvatarId, "no-avatar", "/users/no-avatar", 1), null)]);

        var result = await new PublicCheckInProjector(addresses.Object, profiles.Object).ProjectAsync(visits, ct);

        Assert.Equal(visits.Select(x => x.Id), result.Select(x => x.Id));
        Assert.Equal(new[] { avatarUrl, avatarUrl, null, null, null }, result.Select(x => x.AuthorAvatarUrl));
        Assert.Equal("no-avatar", result[2].Author!.Slug);
        Assert.Null(result[3].Author);
        Assert.Null(result[4].Author);
        addresses.VerifyAll();
        profiles.Verify(x => x.GetProfilesByIdsAsync(It.IsAny<Guid[]>(), ct), Times.Once);
        profiles.VerifyNoOtherCalls();
    }
}
