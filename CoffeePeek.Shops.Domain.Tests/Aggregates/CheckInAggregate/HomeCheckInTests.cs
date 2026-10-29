using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CheckInAggregate;

public class HomeCheckInTests
{
    private static readonly DateTime PreparedAtUtc = new(2026, 9, 20, 22, 15, 0, DateTimeKind.Utc);
    private static readonly DateOnly PreparedOn = new(2026, 9, 21);

    [Fact]
    public void OneBatchIsOnePrivateCheckInWithItsOwnLocalPreparationDate()
    {
        var productId = Guid.NewGuid();
        var checkIn = CheckIn.CreateHome(Guid.NewGuid(), "author", "  Made a batch for friends  ",
            PreparedAtUtc, PreparedOn, "batch_drip", productId, "Honey Bloom", 75m, 360, 4);

        Assert.Equal(CheckInKind.Home, checkIn.Kind);
        Assert.Null(checkIn.ShopId);
        Assert.Equal(PreparedAtUtc, checkIn.VisitedAt);
        Assert.Equal(PreparedOn, checkIn.PreparedOn);
        Assert.Equal("Made a batch for friends", checkIn.Note);
        Assert.Equal("batch_drip", checkIn.BrewMethodCode);
        Assert.Equal(productId, checkIn.CoffeeProductId);
        Assert.Equal("Honey Bloom", checkIn.BeanNameSnapshot);
        Assert.Equal(75m, checkIn.DoseGrams);
        Assert.Equal(360, checkIn.BrewDurationSeconds);
        Assert.Equal(4, checkIn.TasteScore);
        Assert.Null(checkIn.Rating);
        Assert.Equal(CheckInVisibility.Private, checkIn.Visibility);
        Assert.Equal(CheckInModerationState.NotSubmitted, checkIn.ModerationState);
        Assert.False(checkIn.IsPubliclyVisible);
    }

    [Fact]
    public void OptionalPreparationDetailsCanBeUnknownAndLaterEditedWithoutChangingIdentity()
    {
        var checkIn = CheckIn.CreateHome(Guid.NewGuid(), "author", null, PreparedAtUtc,
            PreparedOn, null, null, null, null, null, null);
        var id = checkIn.Id;
        var version = checkIn.RowVersion;
        Assert.Null(checkIn.BrewMethodCode);
        Assert.Null(checkIn.BeanNameSnapshot);
        Assert.Null(checkIn.DoseGrams);
        Assert.Null(checkIn.TasteScore);

        var earlierUtc = PreparedAtUtc.AddDays(-2);
        var earlierLocalDate = PreparedOn.AddDays(-2);
        checkIn.UpdateHome("Now remembered", earlierUtc, earlierLocalDate,
            "aeropress", null, "Local roast", 18.5m, 150, 5);

        Assert.Equal(id, checkIn.Id);
        Assert.Equal(earlierUtc, checkIn.VisitedAt);
        Assert.Equal(earlierLocalDate, checkIn.PreparedOn);
        Assert.Equal("Local roast", checkIn.BeanNameSnapshot);
        Assert.Equal(18.5m, checkIn.DoseGrams);
        Assert.Equal(5, checkIn.TasteScore);
        Assert.NotEqual(version, checkIn.RowVersion);
        Assert.Equal(2, checkIn.ContentRevision);
        Assert.False(checkIn.IsPubliclyVisible);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void TasteScoreMustBeFromOneThroughFive(int invalidScore)
    {
        Assert.Throws<DomainException>(() => CheckIn.CreateHome(Guid.NewGuid(), "author", null,
            PreparedAtUtc, PreparedOn, null, null, null, null, null, invalidScore));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10000.01)]
    [InlineData(18.123)]
    public void DoseMustBeAPositiveDryCoffeeAmountWithTwoDecimalPlaces(decimal invalidDose)
    {
        Assert.Throws<DomainException>(() => CheckIn.CreateHome(Guid.NewGuid(), "author", null,
            PreparedAtUtc, PreparedOn, null, null, "Custom beans", invalidDose, null, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(604801)]
    public void DurationMustBeAPositiveTotalBrewDuration(int invalidSeconds)
    {
        Assert.Throws<DomainException>(() => CheckIn.CreateHome(Guid.NewGuid(), "author", null,
            PreparedAtUtc, PreparedOn, null, null, null, null, invalidSeconds, null));
    }

    [Fact]
    public void BeanAndLocalDateInputsAreValidatedWithoutFabricatingValues()
    {
        var unknown = CheckIn.CreateHome(Guid.NewGuid(), "author", null, PreparedAtUtc,
            PreparedOn, null, null, "   ", null, null, null);
        Assert.Null(unknown.BeanNameSnapshot);
        Assert.Throws<DomainException>(() => CheckIn.CreateHome(Guid.NewGuid(), "author", null,
            PreparedAtUtc, PreparedOn, null, Guid.NewGuid(), null, null, null, null));
        Assert.Throws<DomainException>(() => CheckIn.CreateHome(Guid.NewGuid(), "author", null,
            PreparedAtUtc, PreparedOn.AddDays(2), null, null, null, null, null, null));
        Assert.Throws<DomainException>(() => CheckIn.CreateHome(Guid.NewGuid(), "author", null,
            PreparedAtUtc, PreparedOn, "unknown_method", null, null, null, null, null));
    }

    [Fact]
    public void HomePublicationRequiresCurrentApprovalAndDeletionEndsEdits()
    {
        var checkIn = CheckIn.CreateHome(Guid.NewGuid(), "author", null, PreparedAtUtc,
            PreparedOn, "espresso", null, "Custom beans", 18m, 30, 5);

        checkIn.ChangeVisibility(CheckInVisibility.Public);
        Assert.Equal(CheckInModerationState.Pending, checkIn.ModerationState);
        Assert.False(checkIn.IsPubliclyVisible);
        var revision = checkIn.ContentRevision;
        var submission = Guid.NewGuid();
        Assert.True(checkIn.ApplyModerationDecision(revision, submission, 1,
            CheckInModerationState.Approved, null, DateTime.UtcNow));
        Assert.True(checkIn.IsPubliclyVisible);
        checkIn.UpdateHome("edited", PreparedAtUtc, PreparedOn, "espresso", null, "Beans", 18m, 30, 4);
        Assert.Equal(CheckInVisibility.Public, checkIn.Visibility);
        Assert.False(checkIn.IsPubliclyVisible);
        Assert.False(checkIn.ApplyModerationDecision(revision, submission, 2,
            CheckInModerationState.Approved, null, DateTime.UtcNow));
        var editedRevision = checkIn.ContentRevision;
        checkIn.ChangeVisibility(CheckInVisibility.Private);
        Assert.False(checkIn.ApplyModerationDecision(editedRevision, Guid.NewGuid(), 1,
            CheckInModerationState.Approved, null, DateTime.UtcNow));
        Assert.Equal(CheckInModerationState.NotSubmitted, checkIn.ModerationState);

        checkIn.Delete();
        Assert.NotNull(checkIn.DeletedAtUtc);
        Assert.Throws<DomainException>(() => checkIn.UpdateHome(null, PreparedAtUtc, PreparedOn,
            null, null, null, null, null, null));
    }

    [Fact]
    public void BeanSourceKeepsTheBagAndATrimmedRoasterAndRejectsInvalidValues()
    {
        var checkIn = CheckIn.CreateHome(Guid.NewGuid(), "author", null, PreparedAtUtc, PreparedOn,
            null, null, "Gift from Kenya", null, null, null);
        var bagId = Guid.NewGuid();

        checkIn.SetBeanSource(bagId, "  Sibaristica  ");
        Assert.Equal(bagId, checkIn.ShelfBagId);
        Assert.Equal("Sibaristica", checkIn.RoasterNameSnapshot);

        checkIn.SetBeanSource(null, " ");
        Assert.Null(checkIn.ShelfBagId);
        Assert.Null(checkIn.RoasterNameSnapshot);

        Assert.Throws<DomainException>(() => checkIn.SetBeanSource(Guid.Empty, null));
        Assert.Throws<DomainException>(() => checkIn.SetBeanSource(bagId, new string('r', 251)));
    }

    [Fact]
    public void BagCardIsASeparatePhotoThatNeedsALinkedBagAndCanBeDetached()
    {
        var owner = Guid.NewGuid();
        var checkIn = CheckIn.CreateHome(owner, "author", null, PreparedAtUtc, PreparedOn,
            null, null, "Gift from Kenya", null, null, null);
        var card = new CheckInPhoto("shelf-bag.jpg", "image/jpeg", "check-ins/copy.jpg", 100, owner, 0);
        Assert.Throws<DomainException>(() => checkIn.AttachShelfBagPhoto(card));

        var bagId = Guid.NewGuid();
        checkIn.SetBeanSource(bagId, null);
        checkIn.AttachShelfBagPhoto(card);
        Assert.Equal(card.Id, checkIn.ShelfBagPhotoId);
        Assert.Equal(1, checkIn.NextPhotoSortIndex());
        Assert.Throws<DomainException>(() => checkIn.SetBeanSource(Guid.NewGuid(), null));

        Assert.Equal(card.Id, checkIn.DetachShelfBagPhoto());
        Assert.Null(checkIn.ShelfBagPhotoId);
        Assert.Empty(checkIn.ShopPhotos);
        Assert.Null(checkIn.DetachShelfBagPhoto());
    }
}
