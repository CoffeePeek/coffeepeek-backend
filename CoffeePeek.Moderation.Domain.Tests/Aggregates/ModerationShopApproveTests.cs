using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Aggregates;

public class ModerationShopApproveTests
{
    private static ModerationShop CreatePendingShop() =>
        ModerationShop.Create("Test Cafe", Guid.NewGuid(), Guid.NewGuid(), description: null);

    [Fact]
    public void Approve_WhenLocationIsNull_ThrowsDomainException()
    {
        var shop = CreatePendingShop();

        var act = () => shop.Approve();

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot approve shop with unvalidated address.");
        shop.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Fact]
    public void Approve_WhenLocationIsNotValidated_ThrowsDomainException()
    {
        var shop = CreatePendingShop();
        shop.SetLocation(new ModerationLocation("Unvalidated street"));

        var act = () => shop.Approve();

        act.Should().Throw<DomainException>()
            .WithMessage("Cannot approve shop with unvalidated address.");
        shop.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Fact]
    public void Approve_WhenLocationIsValidated_ReturnsTrueAndSetsApproved()
    {
        var shop = CreatePendingShop();
        shop.SetLocation(new ModerationLocation("Validated street", 53.9m, 27.5m));

        var changed = shop.Approve();

        changed.Should().BeTrue();
        shop.ModerationStatus.Should().Be(ModerationStatus.Approved);
    }

    [Fact]
    public void Approve_WhenAlreadyApproved_ReturnsFalseWithoutThrowing()
    {
        var shop = CreatePendingShop();
        shop.SetLocation(new ModerationLocation("Validated street", 53.9m, 27.5m));
        shop.Approve();

        var changed = shop.Approve();

        changed.Should().BeFalse();
        shop.ModerationStatus.Should().Be(ModerationStatus.Approved);
    }

    [Fact]
    public void Create_StoresCoffeeFocus()
    {
        var shop = ModerationShop.Create(
            "Specialty Cafe",
            Guid.NewGuid(),
            Guid.NewGuid(),
            description: null,
            CoffeePeek.Moderation.Domain.Aggregates.Enums.CoffeeFocus.Specialty);

        shop.CoffeeFocus.Should().Be(CoffeePeek.Moderation.Domain.Aggregates.Enums.CoffeeFocus.Specialty);
    }

    [Fact]
    public void UpdateInfo_ChangesCoffeeFocus()
    {
        var shop = CreatePendingShop();

        shop.UpdateInfo(
            name: null,
            description: null,
            priceRange: null,
            cityId: null,
            CoffeePeek.Moderation.Domain.Aggregates.Enums.CoffeeFocus.CoffeeBar);

        shop.CoffeeFocus.Should().Be(CoffeePeek.Moderation.Domain.Aggregates.Enums.CoffeeFocus.CoffeeBar);
    }

    [Fact]
    public void Reject_WithValidReason_SetsRejectedStatusAndReason()
    {
        var shop = CreatePendingShop();

        shop.Reject("Duplicate submission");

        shop.ModerationStatus.Should().Be(ModerationStatus.Rejected);
        shop.RejectedReason.Should().Be("Duplicate submission");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithNullOrWhitespaceReason_ThrowsDomainException(string? reason)
    {
        var shop = CreatePendingShop();

        var act = () => shop.Reject(reason!);

        act.Should().Throw<DomainException>()
            .WithMessage("Reject reason is required.");
        shop.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Fact]
    public void Reject_WithReasonExceedingMaxLength_ThrowsDomainException()
    {
        var shop = CreatePendingShop();
        var tooLongReason = new string('a', BusinessConstants.MaxRejectReasonCommentLength + 1);

        var act = () => shop.Reject(tooLongReason);

        act.Should().Throw<DomainException>()
            .WithMessage($"reason must be between {BusinessConstants.MinRejectReasonCommentLength} and {BusinessConstants.MaxRejectReasonCommentLength} characters.");
        shop.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Fact]
    public void UpdateContacts_WithValidValues_SetsContact()
    {
        var shop = CreatePendingShop();

        shop.UpdateContacts("+1234567890", "https://instagram.com/coffeepeek", "contact@coffeepeek.com", "https://coffeepeek.com");

        shop.Contact.Should().NotBeNull();
        shop.Contact!.PhoneNumber.Should().Be("+1234567890");
        shop.Contact.InstagramLink.Should().Be("https://instagram.com/coffeepeek");
        shop.Contact.Email.Should().Be("contact@coffeepeek.com");
        shop.Contact.SiteLink.Should().Be("https://coffeepeek.com");
    }

    [Fact]
    public void UpdateContacts_WithPhoneNumberExceedingMaxLength_ThrowsDomainException()
    {
        var shop = CreatePendingShop();
        var phoneNumber = new string('1', BusinessConstants.MaxShopContactPhoneNumberLength + 1);

        var act = () => shop.UpdateContacts(phoneNumber, null, null, null);

        act.Should().Throw<DomainException>();
        shop.Contact.Should().BeNull();
    }

    [Fact]
    public void UpdateContacts_CalledAgain_ReplacesPreviousContact()
    {
        var shop = CreatePendingShop();
        shop.UpdateContacts("+1234567890", null, null, null);

        shop.UpdateContacts(null, "https://instagram.com/newhandle", null, null);

        shop.Contact!.PhoneNumber.Should().BeNull();
        shop.Contact.InstagramLink.Should().Be("https://instagram.com/newhandle");
    }

    [Fact]
    public void UpdateSchedules_WithIntervals_AddsScheduleWithIntervals()
    {
        var shop = CreatePendingShop();
        var schedules = new List<(DayOfWeek DayOfWeek, List<(TimeSpan OpenTime, TimeSpan CloseTime)> Intervals)>
        {
            (DayOfWeek.Monday, [(TimeSpan.FromHours(8), TimeSpan.FromHours(18))]),
            (DayOfWeek.Sunday, []),
        };

        shop.UpdateSchedules(schedules);

        shop.Schedules.Should().HaveCount(2);

        var monday = shop.Schedules.Single(s => s.DayOfWeek == DayOfWeek.Monday);
        monday.IsClosed.Should().BeFalse();
        monday.Intervals.Should().ContainSingle(i =>
            i.OpenTime == TimeSpan.FromHours(8) && i.CloseTime == TimeSpan.FromHours(18));

        var sunday = shop.Schedules.Single(s => s.DayOfWeek == DayOfWeek.Sunday);
        sunday.IsClosed.Should().BeTrue();
        sunday.Intervals.Should().BeEmpty();
    }

    [Fact]
    public void UpdateSchedules_CalledAgain_ReplacesPreviousSchedules()
    {
        var shop = CreatePendingShop();
        shop.UpdateSchedules(new List<(DayOfWeek DayOfWeek, List<(TimeSpan OpenTime, TimeSpan CloseTime)> Intervals)>
        {
            (DayOfWeek.Monday, [(TimeSpan.FromHours(8), TimeSpan.FromHours(18))]),
        });

        shop.UpdateSchedules(new List<(DayOfWeek DayOfWeek, List<(TimeSpan OpenTime, TimeSpan CloseTime)> Intervals)>
        {
            (DayOfWeek.Tuesday, [(TimeSpan.FromHours(9), TimeSpan.FromHours(17))]),
        });

        shop.Schedules.Should().ContainSingle();
        shop.Schedules.Single().DayOfWeek.Should().Be(DayOfWeek.Tuesday);
    }

    [Fact]
    public void AddPhoto_WithValidData_AddsPhotoOwnedByShop()
    {
        var userId = Guid.NewGuid();
        var shop = ModerationShop.Create("Test Cafe", userId, Guid.NewGuid(), description: null);

        shop.AddPhoto("cafe.jpg", "image/jpeg", "storage/key/cafe.jpg", 2048);

        shop.ShopPhotos.Should().ContainSingle();
        var photo = shop.ShopPhotos.Single();
        photo.FileName.Should().Be("cafe.jpg");
        photo.ContentType.Should().Be("image/jpeg");
        photo.StorageKey.Should().Be("storage/key/cafe.jpg");
        photo.SizeBytes.Should().Be(2048);
        photo.OwnerId.Should().Be(userId);
        photo.ModerationShopId.Should().Be(shop.Id);
    }

    [Fact]
    public void AddPhoto_CalledMultipleTimes_AccumulatesPhotos()
    {
        var shop = CreatePendingShop();

        shop.AddPhoto("first.jpg", "image/jpeg", "storage/first.jpg", 1024);
        shop.AddPhoto("second.jpg", "image/jpeg", "storage/second.jpg", 2048);

        shop.ShopPhotos.Should().HaveCount(2);
    }

    [Fact]
    public void AddShopId_WithValidId_SetsShopId()
    {
        var shop = CreatePendingShop();
        var shopId = Guid.NewGuid();

        shop.AddShopId(shopId);

        shop.ShopId.Should().Be(shopId);
    }

    [Fact]
    public void AddShopId_WithEmptyGuid_DoesNotChangeShopId()
    {
        var shop = CreatePendingShop();
        var shopId = Guid.NewGuid();
        shop.AddShopId(shopId);

        shop.AddShopId(Guid.Empty);

        shop.ShopId.Should().Be(shopId);
    }

    [Fact]
    public void Reject_WhenAlreadyApproved_Throws()
    {
        // Regression: rejecting an approved shop left it live in the catalog and re-approve re-published it.
        var shop = CreatePendingShop();
        shop.SetLocation(new ModerationLocation("Validated street", 53.9m, 27.5m));
        shop.Approve();

        var act = () => shop.Reject("Changed my mind");

        act.Should().Throw<DomainException>();
        shop.ModerationStatus.Should().Be(ModerationStatus.Approved);
    }
}
