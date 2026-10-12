using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.MenuAggregate;

public sealed class ShopMenuPhotoTests
{
    [Fact]
    public void Create_WithMediaPhotoId_UsesStableRowIdentityForRetries()
    {
        var mediaId = Guid.NewGuid();

        var first = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/a.jpg", 128, mediaId);
        var retry = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/a.jpg", 128, mediaId);

        Assert.Equal(mediaId, first.Id);
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(mediaId, first.MediaPhotoId);
    }
}
