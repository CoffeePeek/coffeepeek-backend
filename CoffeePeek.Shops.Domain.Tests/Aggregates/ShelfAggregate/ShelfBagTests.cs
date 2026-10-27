using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.ShelfAggregate;

public class ShelfBagTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly ShelfBagPhoto Card = new(Guid.NewGuid(), "shelf-bags/0a.jpg", "image/jpeg");

    private static ShelfBag Create(ShelfBagDetails details, ShelfBagPhoto? photo = null) =>
        ShelfBag.Create(Guid.NewGuid(), Guid.NewGuid(), details, photo, Now);

    private static ShelfBagDetails Details(string? name = "Nut'n'Berry", string? roaster = "Sibaristica", int? weight = 250,
        DateOnly? roastedOn = null, Guid? productId = null, ShelfBagGrind? grind = ShelfBagGrind.Beans) =>
        new(productId, name, roaster, weight, grind, roastedOn);

    [Fact]
    public void CreateTrimsTextAndKeepsTheSnapshot()
    {
        var productId = Guid.NewGuid();
        var bag = Create(Details("  Nut'n'Berry ", "  Sibaristica ", productId: productId, roastedOn: new DateOnly(2026, 10, 12)));
        Assert.Equal(ShelfBagStatus.OnShelf, bag.Status);
        Assert.Equal("Nut'n'Berry", bag.Name);
        Assert.Equal("Sibaristica", bag.RoasterName);
        Assert.Equal(productId, bag.CoffeeProductId);
        Assert.Equal(Now, bag.AddedAtUtc);
        Assert.Null(bag.PhotoId);
    }

    [Fact]
    public void ABagNeedsAPhotoOrABean()
    {
        Assert.Throws<DomainException>(() => Create(Details(name: "  ")));
        var cardOnly = Create(Details(name: null, roaster: null, weight: null, grind: null), Card);
        Assert.Equal(Card.Id, cardOnly.PhotoId);
        Assert.Null(cardOnly.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public void WeightMustBeOneToTenThousandGrams(int weight)
    {
        Assert.Throws<DomainException>(() => Create(Details(weight: weight)));
        Assert.Equal(10000, Create(Details(weight: 10000)).WeightGrams);
        Assert.Equal(1, Create(Details(weight: 1)).WeightGrams);
    }

    [Fact]
    public void TextIsLimitedToTwoHundredFiftyCharacters()
    {
        Assert.Throws<DomainException>(() => Create(Details(name: new string('n', 251))));
        Assert.Throws<DomainException>(() => Create(Details(roaster: new string('r', 251))));
        Assert.Equal(250, Create(Details(name: new string('n', 250))).Name!.Length);
    }

    [Fact]
    public void RoastDateMayBeAtMostTomorrow()
    {
        var today = DateOnly.FromDateTime(Now);
        Assert.Equal(today.AddDays(1), Create(Details(roastedOn: today.AddDays(1))).RoastedOn);
        Assert.Throws<DomainException>(() => Create(Details(roastedOn: today.AddDays(2))));
    }

    [Fact]
    public void UnknownGrindIsRejected() =>
        Assert.Throws<DomainException>(() => Create(Details(grind: (ShelfBagGrind)7)));

    [Fact]
    public void UpdateReplacesDetailsKeepsAnOmittedCardAndReportsAReplacedOne()
    {
        var bag = Create(Details(), Card);
        Assert.Null(bag.UpdateDetails(new ShelfBagDetails(null, null, null, null, null, null), null, Now.AddHours(1)));
        Assert.Equal(Card.Id, bag.PhotoId);
        Assert.Null(bag.Name);
        Assert.Null(bag.RoasterName);
        Assert.Null(bag.WeightGrams);
        Assert.Null(bag.Grind);

        var retake = new ShelfBagPhoto(Guid.NewGuid(), "shelf-bags/0b.png", "image/png");
        Assert.Equal(Card.Id, bag.UpdateDetails(Details(), retake, Now.AddHours(2)));
        Assert.Equal(retake.Id, bag.PhotoId);
        Assert.Null(bag.UpdateDetails(Details(), retake, Now.AddHours(3)));
    }

    [Fact]
    public void ClearingTheBeanOfABagWithoutACardIsRejected()
    {
        var bag = Create(Details());
        Assert.Throws<DomainException>(() => bag.UpdateDetails(Details(name: null), null, Now));
    }
}
