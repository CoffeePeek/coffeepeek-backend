using System.Text.Json;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;
using CoffeePeek.Shops.Persistance.Community;
using CoffeePeek.Shops.Persistance.Configuration;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

internal static class PhotoAdvertisementTestData
{
    internal static IAuthorVisibilityLookup Visibility()
    {
        var result = new Mock<IAuthorVisibilityLookup>();
        result.Setup(x => x.ExcludedUserIdsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return result.Object;
    }

    internal static async Task<(CheckIn Shop, CheckIn Home)> SeedAsync(ShopsDbContext db, Guid owner, CancellationToken ct)
    {
        var city = new City("Photo delivery " + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("Photo delivery shop", city.Id);
        db.Cities.Add(city); db.Shops.Add(shop);
        var now = DateTime.UtcNow.AddMinutes(-1);
        var visit = CheckIn.CreateUnified(owner, shop.Id, "photo-author", null, null, now);
        var home = CheckIn.CreateHome(owner, "photo-author", null, now, DateOnly.FromDateTime(now),
            null, null, null, null, null, null);
        foreach (var row in new[] { visit, home })
        {
            row.AddPhotos([Photo(owner, "jpeg")]);
            row.AddDrinkPhoto(Photo(owner, "png"));
            row.ChangeVisibility(CheckInVisibility.Public);
            row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, now);
            db.CheckIns.Add(row);
        }
        await db.SaveChangesAsync(ct);
        foreach (var row in new[] { visit, home })
            foreach (var photo in row.ShopPhotos)
                db.CheckInPhotoVariantManifests.Add(new CheckInPhotoVariantManifest
                {
                    PhotoId = photo.Id, CheckInId = row.Id,
                    SourceVersion = CheckInPhotoVariantIdentity.SourceVersion(photo.Id, photo.StorageKey), RecipeVersion = 1,
                    VariantsJson = JsonSerializer.Serialize(new[] { "thumb", "feed", "detail" }
                        .SelectMany(p => new[] { "webp", photo.ContentType == "image/png" ? "png" : "jpeg" }
                            .Select(f => new CheckInPhotoVariantInfo(p, f, "image/" + f, 100, 200, 123, new string('a', 64)))))
                });
        await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
        return (visit, home);
    }

    internal static void AssertPhotos(Guid checkInId, CheckInPhotoDto[] photos, CheckInPhotoDto? drink, bool advertised)
    {
        Assert.NotNull(drink);
        foreach (var photo in photos.Append(drink!))
        {
            Assert.Equal($"/api/v1/check-ins/{checkInId}/photos/{photo.Id}", photo.Url);
            if (advertised) Assert.Equal(6, photo.Variants!.Length);
            else Assert.Null(photo.Variants);
        }
    }

    private static CheckInPhoto Photo(Guid owner, string format)
    {
        var id = Guid.NewGuid();
        return new CheckInPhoto("source." + format, "image/" + format, $"check-ins/{id:N}.{format}", 123, owner, id: id);
    }
}
