using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CoffeeShopAggregate;

public sealed class ShopCollectionProtectionTests
{
    [Fact]
    public void Schedule_DetachesInputAndRejectsMutationThroughReturnedIntervals()
    {
        var intervals = new List<ShopScheduleInterval> { ShopScheduleInterval.Create(TimeSpan.FromHours(8), TimeSpan.FromHours(18)) };
        var day = ShopSchedule.Create(DayOfWeek.Monday, false, intervals);
        var shop = Shop(); shop.AddSchedule([day]);
        intervals.Clear();
        Assert.Single(Assert.Single(shop.Schedules).Intervals);
        Assert.Throws<NotSupportedException>(() => day.Intervals.Clear());
        Assert.Throws<DomainException>(() => shop.AddSchedule([ShopSchedule.Create(DayOfWeek.Monday, true, [])]));
        Assert.Equal(day, Assert.Single(shop.Schedules));
    }

    [Fact]
    public void Gallery_EnumeratesAndValidatesWholeBatchBeforeChangingObjectsOrCollection()
    {
        var shop = Shop(); var existing = Photo("existing"); shop.AddPhotos([existing]);
        var first = Photo("first", 8);
        Assert.Throws<InvalidOperationException>(() => shop.AddPhotos(FailingBatch()));
        Assert.Equal(8, first.SortIndex); Assert.Equal(existing, Assert.Single(shop.ShopPhotos));
        Assert.Throws<DomainException>(() => shop.AddPhotos([first, Photo("existing")]));
        Assert.Equal(8, first.SortIndex); Assert.Equal(existing, Assert.Single(shop.ShopPhotos));
        Assert.Null(typeof(ShopPhoto).GetMethod("SetSortIndex"));
        IEnumerable<ShopPhoto> FailingBatch() { yield return first; throw new InvalidOperationException("Enumeration failed."); }
    }

    [Fact]
    public void Gallery_RejectsNullAndDuplicateIdBeforeMutation()
    {
        var shop = Shop(); var first = Photo("first");
        Assert.Throws<DomainException>(() => shop.AddPhotos([first, null!]));
        Assert.Throws<DomainException>(() => shop.AddPhotos([first, new("second.jpg", "image/jpeg", "second", 1, Guid.NewGuid(), id: first.Id)]));
        Assert.Empty(shop.ShopPhotos);
    }

    private static CoffeeShop Shop() => new(null, "Shop", null, PriceRange.Moderate, Guid.NewGuid());
    private static ShopPhoto Photo(string key, int sort = 0) => new(key + ".jpg", "image/jpeg", key, 1, Guid.NewGuid(), sort);
}
