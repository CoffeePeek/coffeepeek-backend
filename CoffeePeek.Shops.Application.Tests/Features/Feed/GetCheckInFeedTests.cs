#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Application.PublicAddresses;
using Moq;
using Xunit;

namespace CoffeePeek.Shops.Application.Tests.Features.Feed;

public class GetCheckInFeedTests
{
    [Fact]
    public async Task GlobalFeedComposesAllAuthorsAndKeepsAddressLookupsBatched()
    {
        var ct = TestContext.Current.CancellationToken;
        var readerId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var time = DateTime.UtcNow.AddDays(-1);
        var rows = new[]
        {
            new CheckInFeedEntry(new UnifiedCheckInDto { Id = Guid.NewGuid(), ShopId = shopId, UserId = user1, Text = "First", IsHelpfulByCurrentUser = true }, time),
            new CheckInFeedEntry(new UnifiedCheckInDto { Id = Guid.NewGuid(), ShopId = shopId, UserId = user2, Text = "Second" }, time.AddHours(-1))
        };
        var reader = new Mock<ICheckInFeedReader>();
        reader.Setup(x => x.ReadAsync(new CheckInFeedScope(), 2, null, readerId, ct)).ReturnsAsync(new CheckInFeedReadPage(rows, true));
        var addresses = new Mock<IPublicAddressService>();
        addresses.Setup(x => x.GetForReadAsync(PublicAddressKind.CoffeeShop,
            It.Is<Guid[]>(ids => ids.SequenceEqual(new[] { shopId })), ct))
            .ReturnsAsync([new(shopId, "shop", "/coffee-shops/shop", 1)]);
        var profiles = new Mock<IProfileAddressLookup>();
        const string avatarUrl = "https://media.example.com/coffeepeek.avatars/example-author.jpg";
        profiles.Setup(x => x.GetProfilesByIdsAsync(It.Is<Guid[]>(ids => ids.SequenceEqual(new[] { user1, user2 })), ct))
            .ReturnsAsync([new(new(user1, "author-one", "/users/author-one", 1), avatarUrl),
                new(new(user2, "author-two", "/users/author-two", 1), null)]);
        var response = await GetCheckInFeedHandler.Handle(new(readerId, PageSize: 2), reader.Object,
            addresses.Object, profiles.Object, new PublicCheckInProjector(addresses.Object, profiles.Object), ct);

        Assert.True(response.IsSuccess);
        Assert.Equal(new[] { "author-one", "author-two" }, response.Data.Items.Select(x => x.CheckIn.Author!.Slug));
        Assert.Equal(avatarUrl, response.Data.Items[0].CheckIn.AuthorAvatarUrl);
        Assert.Null(response.Data.Items[1].CheckIn.AuthorAvatarUrl);
        Assert.Equal(new[] { "First", "Second" }, response.Data.Items.Select(x => x.CheckIn.Text));
        Assert.True(response.Data.Items[0].CheckIn.IsHelpfulByCurrentUser);
        Assert.Equal(time, response.Data.Items[0].PublishedAtUtc);
        Assert.Equal(new CheckInFeedPosition(rows[1].PublishedAtUtc, rows[1].CheckIn.Id),
            CheckInFeedCursor.Decode(response.Data.NextCursor!, new()));
        addresses.Verify(x => x.GetForReadAsync(PublicAddressKind.CoffeeShop, It.IsAny<Guid[]>(), ct), Times.Once);
        profiles.Verify(x => x.GetProfilesByIdsAsync(It.IsAny<Guid[]>(), ct), Times.Once);
    }

    [Fact]
    public async Task CanonicalScopeResolvesAliasesAndIsPassedWithCursorToReader()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = new CheckInFeedScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var position = new CheckInFeedPosition(DateTime.UtcNow.AddDays(-1), Guid.NewGuid());
        var addresses = new Mock<IPublicAddressService>();
        addresses.Setup(x => x.ResolveAsync(PublicAddressKind.City, "city", ct)).ReturnsAsync(new PublicAddressMetadata(scope.CityId!.Value, "city", "/cities/city", 1));
        addresses.Setup(x => x.ResolveAsync(PublicAddressKind.CoffeeShop, "old-shop-alias", ct)).ReturnsAsync(new PublicAddressMetadata(scope.ShopId!.Value, "current-shop", "/coffee-shops/current-shop", 2, true));
        var profiles = new Mock<IProfileAddressLookup>();
        profiles.Setup(x => x.ResolveAsync("author", ct)).ReturnsAsync(new PublicAddressMetadata(scope.AuthorId!.Value, "author", "/users/author", 1));
        var reader = new Mock<ICheckInFeedReader>();
        reader.Setup(x => x.ReadAsync(scope, 20, position, null, ct)).ReturnsAsync(new CheckInFeedReadPage([], false));
        var response = await GetCheckInFeedHandler.Handle(new(null, "city", "old-shop-alias", "author", Cursor: CheckInFeedCursor.Encode(position, scope)),
            reader.Object, addresses.Object, profiles.Object, new PublicCheckInProjector(addresses.Object, profiles.Object), ct);
        Assert.Empty(response.Data.Items);
        Assert.Null(response.Data.NextCursor);
        reader.VerifyAll();
        profiles.Verify(x => x.GetProfilesByIdsAsync(It.IsAny<Guid[]>(), ct), Times.Never);
    }

    [Theory]
    [InlineData("city")]
    [InlineData("shop")]
    [InlineData("author")]
    public async Task UnknownFiltersNeverFallBackToGlobalFeed(string filter)
    {
        var reader = new Mock<ICheckInFeedReader>(MockBehavior.Strict);
        var query = new GetCheckInFeedQuery(null, CitySlug: filter == "city" ? "missing" : null,
            CoffeeShopSlug: filter == "shop" ? "missing" : null, AuthorSlug: filter == "author" ? "missing" : null);
        await Assert.ThrowsAsync<NotFoundException>(() => GetCheckInFeedHandler.Handle(query, reader.Object,
            Mock.Of<IPublicAddressService>(), Mock.Of<IProfileAddressLookup>(), Mock.Of<IPublicCheckInProjector>(), TestContext.Current.CancellationToken));
        reader.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(101, null)]
    [InlineData(20, " ")]
    public async Task InvalidQueryIsRejectedBeforeAnyLookups(int pageSize, string? city)
    {
        await Assert.ThrowsAsync<ValidationException>(() => GetCheckInFeedHandler.Handle(new(null, CitySlug: city, PageSize: pageSize),
            Mock.Of<ICheckInFeedReader>(MockBehavior.Strict), Mock.Of<IPublicAddressService>(MockBehavior.Strict),
            Mock.Of<IProfileAddressLookup>(MockBehavior.Strict), Mock.Of<IPublicCheckInProjector>(MockBehavior.Strict), TestContext.Current.CancellationToken));
    }
}
