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
        var cup = new CheckInPhoto("cup.png", "image/png", "cup", 10, row.UserId, 1);
        Assert.Throws<DomainException>(() => row.AddDrinkPhoto(cup));
        var original = new CheckInPhoto("full.jpg", "image/jpeg", "full", 10, row.UserId);
        row.AddPhotos([original]);
        Assert.Throws<DomainException>(() => row.AddDrinkPhoto(original));
        Assert.Throws<DomainException>(() => row.AddDrinkPhoto(new("foreign.png", "image/png", "foreign", 10, Guid.NewGuid())));
        row.AddDrinkPhoto(cup);
        Assert.Equal(cup.Id, row.DrinkPhotoId); Assert.Equal(1, cup.SortIndex);
        Assert.Throws<DomainException>(() => row.AddDrinkPhoto(new("second.png", "image/png", "second", 10, row.UserId)));
        row.UpdateContent("updated", null); row.ChangeVisibility(CheckInVisibility.Public);
        Assert.Equal(cup.Id, row.DrinkPhotoId);
    }

    [Fact]
    public void CutoutCannotBeSharedAndAddingItInvalidatesApproval()
    {
        var row = Create(CheckInVisibility.Public);
        row.AddPhotos([new("full.jpg", "image/jpeg", "full", 10, row.UserId)]);
        row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1,
            CheckInModerationState.Approved, null, DateTime.UtcNow);
        var revision = row.ContentRevision;
        var cup = new CheckInPhoto("cup.png", "image/png", "cup", 10, row.UserId, 1);
        row.AddDrinkPhoto(cup);
        Assert.Equal(row.Id, cup.CanonicalCheckInId);
        Assert.Equal(revision + 1, row.ContentRevision);
        Assert.False(row.IsPubliclyVisible);
        Assert.Equal(CheckInModerationState.Pending, row.ModerationState);
        var other = CheckIn.CreateUnified(row.UserId, row.ShopId, "author", null, null, DateTime.UtcNow);
        other.AddPhotos([new("other.jpg", "image/jpeg", "other", 10, row.UserId)]);
        Assert.Throws<DomainException>(() => other.AddDrinkPhoto(cup));
        row.AddPhotos(Enumerable.Range(1, 4).Select(i =>
            new CheckInPhoto($"{i}.jpg", "image/jpeg", $"extra-{i}", 10, row.UserId, i)));
        Assert.Equal(6, row.ShopPhotos.Count);
        Assert.Throws<DomainException>(() => row.AddPhotos([new("six.jpg", "image/jpeg", "six", 10, row.UserId)]));
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
