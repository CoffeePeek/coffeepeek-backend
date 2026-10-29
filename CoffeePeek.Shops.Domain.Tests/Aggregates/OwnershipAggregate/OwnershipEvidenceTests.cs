using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.OwnershipAggregate;
public class OwnershipEvidenceTests
{
    private static BusinessOwnershipClaim Claim(Guid user, Guid business, Guid[]? photos = null) =>
        BusinessOwnershipClaim.Create(BusinessType.CoffeeShop, business, "Name", "slug", user, "contact", "proof", photos);
    [Fact]
    public void AReservationSurvivesRetriesAndPhotoOrderChanges()
    {
        var owner = Guid.NewGuid(); var business = Guid.NewGuid();
        var photos = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var first = Claim(owner, business, photos);
        var retry = Claim(owner, business, photos.Reverse().ToArray());
        Assert.Equal(first.Id, retry.Id);
        var other = Claim(Guid.NewGuid(), business, photos);
        Assert.NotEqual(first.Id, other.Id);
    }
    [Fact]
    public void DuplicateEmptyAndOversizedSetsAreRejected()
    {
        var claim = Claim(Guid.NewGuid(), Guid.NewGuid()); var photo = Guid.NewGuid();
        Assert.Throws<ValidationException>(() => Claim(Guid.NewGuid(), Guid.NewGuid(), [photo, photo]));
        Assert.Throws<ValidationException>(() => Claim(Guid.NewGuid(), Guid.NewGuid(), [Guid.Empty]));
        Assert.Throws<ValidationException>(() => Claim(Guid.NewGuid(), Guid.NewGuid(), Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray()));
    }
}
