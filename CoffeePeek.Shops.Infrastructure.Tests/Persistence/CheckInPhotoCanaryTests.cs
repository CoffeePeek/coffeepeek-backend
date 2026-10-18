using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Modules;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CheckInPhotoCanaryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ShopsDbContext Context() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void CanaryConfigurationRequiresOneToOneHundredDistinctNonEmptyUserIdsAndFailsClosed()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var options = Canary(first);
        options.AdvertiseCanaryUserIds = $" {first:D}, {second:D} ";
        Assert.True(options.IsCanaryConfigurationValid());
        Assert.True(options.ShouldAdvertiseTo(first)); Assert.True(options.ShouldAdvertiseTo(second));
        Assert.False(options.ShouldAdvertiseTo(null)); Assert.False(options.ShouldAdvertiseTo(Guid.Empty));
        Assert.False(options.ShouldAdvertiseTo(Guid.NewGuid()));
        foreach (var invalid in new[] { "", " ", "not-a-user-id", Guid.Empty.ToString(),
                     $"{first},", $",{first}", $"{first},,{second}", $"{first},{first.ToString().ToUpperInvariant()}" })
        {
            options.AdvertiseCanaryUserIds = invalid;
            Assert.False(options.IsCanaryConfigurationValid()); Assert.False(options.ShouldAdvertiseTo(first));
        }
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        options.AdvertiseCanaryUserIds = string.Join(',', ids.Take(100));
        Assert.True(options.IsCanaryConfigurationValid()); Assert.True(options.ShouldAdvertiseTo(ids[99]));
        options.AdvertiseCanaryUserIds = string.Join(',', ids);
        Assert.False(options.IsCanaryConfigurationValid()); Assert.False(options.ShouldAdvertiseTo(ids[0]));
    }

    [Fact]
    public void DefaultsAndGlobalModePreserveExistingBehaviorAndAdvertiseFlagStillWins()
    {
        var options = new CheckInPhotoDeliveryOptions();
        Assert.False(options.AdvertiseCanaryOnly); Assert.Equal("", options.AdvertiseCanaryUserIds);
        Assert.True(options.IsCanaryConfigurationValid()); Assert.False(options.ShouldAdvertiseTo(null));
        options.AdvertiseVariants = true; options.AdvertiseCanaryUserIds = "unused in global mode";
        Assert.True(options.IsCanaryConfigurationValid()); Assert.True(options.ShouldAdvertiseTo(null));
        Assert.True(options.ShouldAdvertiseTo(Guid.NewGuid()));
        options.AdvertiseCanaryOnly = true; options.AdvertiseCanaryUserIds = Guid.NewGuid().ToString();
        options.AdvertiseVariants = false;
        Assert.False(options.ShouldAdvertiseTo(Guid.Parse(options.AdvertiseCanaryUserIds)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupValidationRejectsInvalidCanaryListEvenBeforeAdvertisementAndDoesNotEchoItsValue(bool advertise)
    {
        const string invalid = "do-not-echo-sensitive-config";
        using var provider = Provider(advertise, true, true, invalid);
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CheckInPhotoDeliveryOptions>>().Value);
        Assert.DoesNotContain(invalid, error.Message);
        Assert.Contains("distinct non-empty user IDs", error.Message);
    }

    [Fact]
    public void StartupValidationRetainsServePrerequisiteAndAcceptsValidCanaryOrExistingGlobalConfiguration()
    {
        using (var invalid = Provider(true, false, false, ""))
            Assert.Contains("requires serving", Assert.Throws<OptionsValidationException>(() => invalid.GetRequiredService<IOptions<CheckInPhotoDeliveryOptions>>().Value).Message);
        using (var canary = Provider(true, true, true, Guid.NewGuid().ToString()))
            Assert.True(canary.GetRequiredService<IOptions<CheckInPhotoDeliveryOptions>>().Value.IsCanaryConfigurationValid());
        using (var global = Provider(true, true, false, ""))
            Assert.True(global.GetRequiredService<IOptions<CheckInPhotoDeliveryOptions>>().Value.ShouldAdvertiseTo(null));
    }

    [Theory]
    [InlineData("listed")]
    [InlineData("other")]
    [InlineData("anonymous")]
    public async Task FeedShopDetailPublicAndHomeDrinkDescriptorsFollowTheViewerRatherThanTheAuthor(string reader)
    {
        await using var db = Context(); var owner = Guid.NewGuid(); var listed = Guid.NewGuid();
        var rows = await PhotoCanaryTestData.SeedAsync(db, owner, Ct);
        Guid? viewer = reader == "listed" ? listed : reader == "other" ? owner : null;
        var options = Options.Create(Canary(listed)); var queries = new CheckInQueries(db, PhotoCanaryTestData.Visibility(), options);
        var expected = reader == "listed";
        var detail = (await queries.GetUnifiedByIdAsync(rows.Shop.Id, viewer, Ct))!;
        PhotoCanaryTestData.AssertPhotos(detail.Id, detail.Photos, detail.DrinkPhoto, expected);
        var home = (await queries.GetHomeByIdAsync(rows.Home.Id, viewer, Ct))!;
        PhotoCanaryTestData.AssertPhotos(home.Id, home.Photos, home.DrinkPhoto, expected);
        var publicPage = await queries.GetPublicAsync(viewer, null, null, owner, 10, null, Ct);
        Assert.Equal(2, publicPage.Items.Length);
        foreach (var item in publicPage.Items) PhotoCanaryTestData.AssertPhotos(item.Id, item.Photos, item.DrinkPhoto, expected);
        var feed = await new CheckInFeedReader(db, PhotoCanaryTestData.Visibility(), options)
            .ReadAsync(new CheckInFeedScope(AuthorId: owner), 10, null, viewer, Ct);
        Assert.Equal(2, feed.Items.Length);
        foreach (var item in feed.Items) PhotoCanaryTestData.AssertPhotos(item.CheckIn.Id, item.CheckIn.Photos, item.CheckIn.DrinkPhoto, expected);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerListsUseTheAuthenticatedOwnerAndModerationNeverAdvertises(bool listedOwner)
    {
        await using var db = Context(); var owner = Guid.NewGuid();
        var rows = await PhotoCanaryTestData.SeedAsync(db, owner, Ct);
        var options = Options.Create(Canary(listedOwner ? owner : Guid.NewGuid()));
        var queries = new CheckInQueries(db, PhotoCanaryTestData.Visibility(), options);
        var mine = Assert.Single((await queries.GetUnifiedByUserAsync(owner, 1, 10, ct: Ct)).Items);
        PhotoCanaryTestData.AssertPhotos(mine.Id, mine.Photos, mine.DrinkPhoto, listedOwner);
        var shop = Assert.Single(await queries.GetByUserAndShopAsync(owner, rows.Shop.ShopId!.Value, ct: Ct));
        PhotoCanaryTestData.AssertPhotos(shop.Id, shop.Photos, shop.DrinkPhoto, listedOwner);
        var home = Assert.Single((await queries.GetHomeByUserAsync(owner, 1, 10, ct: Ct)).Items);
        PhotoCanaryTestData.AssertPhotos(home.Id, home.Photos, home.DrinkPhoto, listedOwner);
        var moderation = (await queries.GetModerationSnapshotAsync(rows.Shop.Id, rows.Shop.ContentRevision, Ct))!;
        PhotoCanaryTestData.AssertPhotos(moderation.Id, moderation.Photos, moderation.DrinkPhoto, false);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    internal static CheckInPhotoDeliveryOptions Canary(Guid viewer) => new()
    {
        ServeVariants = true, AdvertiseVariants = true, AdvertiseCanaryOnly = true,
        AdvertiseCanaryUserIds = viewer.ToString()
    };

    private static ServiceProvider Provider(bool advertise, bool serve, bool canary, string ids)
    {
        var values = new Dictionary<string, string?>
        {
            ["CheckInPhotoDeliveryOptions:AdvertiseVariants"] = advertise.ToString(),
            ["CheckInPhotoDeliveryOptions:ServeVariants"] = serve.ToString(),
            ["CheckInPhotoDeliveryOptions:AdvertiseCanaryOnly"] = canary.ToString(),
            ["CheckInPhotoDeliveryOptions:AdvertiseCanaryUserIds"] = ids
        };
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddCommunityModule(); return services.BuildServiceProvider();
    }
}
