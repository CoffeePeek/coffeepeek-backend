using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CheckInAggregate;

public class UnifiedCheckInTests
{
    private static CheckIn Create(CheckInVisibility visibility = CheckInVisibility.Private) =>
        CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", " coffee ",
            Rating.Create(4, 3, 5), DateTime.UtcNow, visibility);

    [Fact]
    public void CutoutMustBeSeparateOwnedPngWithAnOriginalAndCanOnlyBeAddedOnce()
    {
        var row = Create();
        var cup = new ShopPhoto("cup.png", "image/png", "cup", 10, row.UserId);
        Assert.Throws<DomainException>(() => row.AddDrinkPhoto(cup));
        var original = new ShopPhoto("full.jpg", "image/jpeg", "full", 10, row.UserId);
        row.AddPhotos([original]);
        Assert.Throws<DomainException>(() => row.AddDrinkPhoto(original));
        Assert.Throws<DomainException>(() => row.AddDrinkPhoto(new("foreign.png", "image/png", "foreign", 10, Guid.NewGuid())));
        row.AddDrinkPhoto(cup);
        Assert.Equal(cup.Id, row.DrinkPhotoId); Assert.Equal(1, cup.SortIndex);
        Assert.Throws<DomainException>(() => row.AddDrinkPhoto(new("second.png", "image/png", "second", 10, row.UserId)));
        row.UpdateContent("updated", null); row.ChangeVisibility(CheckInVisibility.Public);
        Assert.Equal(cup.Id, row.DrinkPhotoId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void DateOnlyContentIsAcceptedForBothVisibilities(string? text)
    {
        foreach (var visibility in Enum.GetValues<CheckInVisibility>())
        {
            var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", text, null,
                DateTime.UtcNow, visibility);
            Assert.Equal("", checkIn.Note);
            Assert.Null(checkIn.Rating);
            checkIn.UpdateContent(null, null);
            Assert.Equal("", checkIn.Note);
            Assert.Null(checkIn.Rating);
        }
    }

    [Fact]
    public void TextLimitAppliesToEveryVisibility()
    {
        foreach (var visibility in Enum.GetValues<CheckInVisibility>())
        {
            var c = CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", new string('x',1000), Rating.Create(1,1,1), DateTime.UtcNow, visibility);
            Assert.Equal(1000,c.Note.Length);
            Assert.Throws<DomainException>(() => c.UpdateContent(new string('x',1001), Rating.Create(1,1,1)));
        }
    }

    [Fact]
    public void CreateUsesPrivateDefaultAndTrimsText()
    {
        var checkIn = Create();
        Assert.Equal(CheckInVisibility.Private, checkIn.Visibility);
        Assert.Equal("coffee", checkIn.Note);
        Assert.Equal(1, checkIn.ContentRevision);
        Assert.False(checkIn.IsPubliclyVisible);
    }

    [Fact]
    public void InvalidScoresVisibilityAndFutureTimeAreRejected()
    {
        Assert.Throws<DomainException>(() => Rating.Create(0, 1, 1));
        Assert.Throws<DomainException>(() => Rating.Create(1, 6, 1));
        Assert.Throws<DomainException>(() => Rating.Create(1, 1, 0));
        Assert.Throws<DomainException>(() => CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author",
            null, null, default));
        Assert.Throws<DomainException>(() => CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author",
            "x", Rating.Create(1, 1, 1), DateTime.UtcNow, (CheckInVisibility)2));
        Assert.Throws<DomainException>(() => CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author",
            "x", Rating.Create(1, 1, 1), DateTime.UtcNow.AddDays(1)));
    }

    [Fact]
    public void EditHideDeleteInvalidateApprovalAndChangeRowVersion()
    {
        var checkIn = Create(CheckInVisibility.Public);
        var originalVersion = checkIn.RowVersion;
        var id = checkIn.Id;
        checkIn.UpdateContent("edited", Rating.Create(5, 4, 3));
        Assert.Equal(2, checkIn.ContentRevision);
        Assert.Equal(CheckInModerationState.Pending, checkIn.ModerationState);
        Assert.NotEqual(originalVersion, checkIn.RowVersion);
        checkIn.ChangeVisibility(CheckInVisibility.Private);
        Assert.Equal(3, checkIn.ContentRevision);
        Assert.Equal(CheckInModerationState.NotSubmitted, checkIn.ModerationState);
        checkIn.ChangeVisibility(CheckInVisibility.Private);
        Assert.Equal(3, checkIn.ContentRevision);
        checkIn.Delete();
        Assert.Equal(4, checkIn.ContentRevision);
        Assert.Equal(id, checkIn.Id);
        Assert.Throws<DomainException>(() => checkIn.UpdateContent("restore", Rating.Create(1, 1, 1)));
        Assert.Throws<DomainException>(() => checkIn.ChangeVisibility(CheckInVisibility.Public));
        Assert.False(checkIn.IsPubliclyVisible);
    }
}
