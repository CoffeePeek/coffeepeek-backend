using CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Aggregates;

public class ShopImportCandidateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Reviewer = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void FromOsm_SetsUniqueSourceKey()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/3032203937", "Coffe Joy"), Now);

        candidate.Source.Should().Be(ImportSource.Osm);
        candidate.ExternalId.Should().Be("node/3032203937");
        candidate.QueueStatus.Should().Be(ImportQueueStatus.Pending);
    }

    [Fact]
    public void FromOsm_ClipsPhoneLongerThanColumn()
    {
        var phone = string.Join("; ", Enumerable.Repeat("+375 29 111-22-33", 12));
        phone.Length.Should().BeGreaterThan(ShopImportCandidate.MaxPhoneLength);

        var snapshot = new OsmCandidateSnapshot(
            "node/1",
            "Cafe",
            "Немига 5, Минск",
            53.9152m,
            27.5847m,
            phone,
            null,
            null,
            null,
            null,
            null,
            Now.AddMonths(-1),
            null,
            new Dictionary<string, string> { ["amenity"] = "cafe", ["name"] = "Cafe" });

        var candidate = ShopImportCandidate.FromOsm(snapshot, Now);

        candidate.Phone.Should().Be(phone[..ShopImportCandidate.MaxPhoneLength]);
        candidate.Phone!.Length.Should().Be(ShopImportCandidate.MaxPhoneLength);
    }

    [Fact]
    public void FromOsm_StaleObject_GoesToStaleBucket()
    {
        var snapshot = Snapshot("node/1", "Old Cafe", osmUpdatedAt: Now.AddYears(-6));
        var candidate = ShopImportCandidate.FromOsm(snapshot, Now);

        candidate.CollectorBucket.Should().Be(ImportCollectorBucket.Stale);
        candidate.QueueStatus.Should().Be(ImportQueueStatus.Pending);
    }

    [Fact]
    public void Decide_PublishWithoutFocus_Throws()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Coffe Joy"), Now);

        var act = () => candidate.Decide(
            ImportQueueStatus.Published, null, [], Reviewer, overrideClosed: false, Now);

        act.Should().Throw<DomainException>().WithMessage("*type*");
    }

    [Fact]
    public void Decide_PublishWithoutName_Throws()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", null), Now);

        var act = () => candidate.Decide(
            ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, [], Reviewer, false, Now);

        act.Should().Throw<DomainException>().WithMessage("*name*");
    }

    [Fact]
    public void Decide_Specialty_AddsSpecialtyTag()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Coffe Joy"), Now);

        candidate.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Specialty, ["to_go"], Reviewer, false, Now);

        candidate.TagSlugs.Should().Contain("specialty");
        candidate.TagSlugs.Should().Contain("to_go");
        candidate.CoffeeFocus.Should().Be(ImportCoffeeFocus.Specialty);
        candidate.QueueStatus.Should().Be(ImportQueueStatus.Published);
    }

    [Fact]
    public void Decide_Cafe_DoesNotKeepSpecialtyTag()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Cafe"), Now);

        candidate.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, ["specialty", "bakery"], Reviewer, false, Now);

        candidate.TagSlugs.Should().NotContain("specialty");
        candidate.TagSlugs.Should().Contain("bakery");
    }

    [Fact]
    public void Decide_RejectedWithoutReason_Throws()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Varka"), Now);

        var act = () => candidate.Decide(
            ImportQueueStatus.Rejected, null, [], Reviewer, false, Now, rejectReason: null);

        act.Should().Throw<DomainException>().WithMessage("*Reject reason*");
    }

    [Fact]
    public void Decide_RejectedWithReason_SetsReason()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Varka"), Now);

        candidate.Decide(
            ImportQueueStatus.Rejected, null, [], Reviewer, false, Now, ImportRejectReason.NotCoffee);

        candidate.QueueStatus.Should().Be(ImportQueueStatus.Rejected);
        candidate.RejectReason.Should().Be(ImportRejectReason.NotCoffee);
    }

    [Fact]
    public void Decide_Published_ClearsRejectReason()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Varka"), Now);
        candidate.Decide(
            ImportQueueStatus.Rejected, null, [], Reviewer, false, Now, ImportRejectReason.Closed);

        candidate.Decide(
            ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, [], Reviewer, false, Now);

        candidate.QueueStatus.Should().Be(ImportQueueStatus.Published);
        candidate.RejectReason.Should().BeNull();
    }

    [Fact]
    public void Decide_AlreadyPublishedToCatalog_Throws()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Varka"), Now);
        candidate.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, [], Reviewer, false, Now);
        candidate.AttachPublishedShop(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        var act = () => candidate.Decide(
            ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, [], Reviewer, false, Now);

        act.Should().Throw<DomainException>().WithMessage("Candidate is already published to the catalog.");
    }

    [Fact]
    public void Decide_StuckPublishedWithoutShopId_AllowsRetry()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Varka"), Now);
        candidate.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, [], Reviewer, false, Now);
        candidate.ResultingShopId.Should().BeNull();

        candidate.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Specialty, ["to_go"], Reviewer, false, Now);

        candidate.QueueStatus.Should().Be(ImportQueueStatus.Published);
        candidate.CoffeeFocus.Should().Be(ImportCoffeeFocus.Specialty);
        candidate.ResultingShopId.Should().BeNull();
    }

    [Fact]
    public void RefreshFromOsm_DoesNotResurrectRejected()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Varka"), Now);
        candidate.Decide(
            ImportQueueStatus.Rejected, null, [], Reviewer, false, Now, ImportRejectReason.Invalid);

        candidate.RefreshFromOsm(Snapshot("node/1", "Varka Coffee"), Now.AddDays(1));

        candidate.QueueStatus.Should().Be(ImportQueueStatus.Rejected);
        candidate.RejectReason.Should().Be(ImportRejectReason.Invalid);
        candidate.Name.Should().Be("Varka Coffee");
    }

    [Fact]
    public void RefreshFromOsm_DoesNotOverwritePublishedName()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Varka"), Now);
        candidate.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, [], Reviewer, false, Now);
        var address = candidate.Address;
        var latitude = candidate.Latitude;
        var longitude = candidate.Longitude;

        candidate.RefreshFromOsm(Snapshot("node/1", "Varka Coffee"), Now.AddDays(1));

        candidate.Name.Should().Be("Varka");
        candidate.Address.Should().Be(address);
        candidate.Latitude.Should().Be(latitude);
        candidate.Longitude.Should().Be(longitude);
        candidate.QueueStatus.Should().Be(ImportQueueStatus.Published);
    }

    [Fact]
    public void RefreshFromOsm_DoesNotOverwriteSkippedName()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Varka"), Now);
        candidate.Decide(ImportQueueStatus.Skipped, null, [], Reviewer, false, Now);
        var address = candidate.Address;
        var latitude = candidate.Latitude;
        var longitude = candidate.Longitude;

        candidate.RefreshFromOsm(Snapshot("node/1", "Varka Coffee"), Now.AddDays(1));

        candidate.Name.Should().Be("Varka");
        candidate.Address.Should().Be(address);
        candidate.Latitude.Should().Be(latitude);
        candidate.Longitude.Should().Be(longitude);
        candidate.QueueStatus.Should().Be(ImportQueueStatus.Skipped);
    }

    [Fact]
    public void Decide_GoogleClosedWithoutOverride_Throws()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Closed Bar"), Now);
        candidate.ApplyGoogleStatus(ImportGoogleBusinessStatus.ClosedPermanently, null, Now);

        var act = () => candidate.Decide(
            ImportQueueStatus.Published, ImportCoffeeFocus.CoffeeBar, [], Reviewer, overrideClosed: false, Now);

        act.Should().Throw<DomainException>().WithMessage("*closed*");
    }

    [Fact]
    public void Decide_GoogleClosedWithOverride_Publishes()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Closed Bar"), Now);
        candidate.ApplyGoogleStatus(ImportGoogleBusinessStatus.ClosedPermanently, null, Now);

        candidate.Decide(
            ImportQueueStatus.Published, ImportCoffeeFocus.CoffeeBar, [], Reviewer, overrideClosed: true, Now);

        candidate.QueueStatus.Should().Be(ImportQueueStatus.Published);
    }

    [Fact]
    public void GetResearchLinks_UsesCoordinatesNotNameSearch()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/3032203937", "Больше кофе"), Now);

        var links = candidate.GetResearchLinks();

        links.YandexMaps.Should().Contain("mode=whatshere");
        links.YandexMaps.Should().Contain("27.5847");
        links.YandexMaps.Should().NotContain("text=");
        links.GoogleMaps.Should().Contain("/@");
        links.GoogleMaps.Should().NotContain("Больше");
        links.YandexEmbed.Should().Contain("map-widget");
        links.GoogleEmbed.Should().Contain("output=embed");
        links.InstagramSearch.Should().BeNull();
        links.OsmHistory.Should().Be("https://www.openstreetmap.org/node/3032203937/history");
        links.YandexImages.Should().Contain("l=stv");
        links.StreetViewEmbed.Should().Contain("cbll=53.9152,27.5847");
        links.StreetViewEmbed.Should().Contain("output=embed");
    }

    [Fact]
    public void PatchContacts_SetsNormalizedInstagram()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Cafe"), Now);

        candidate.PatchContacts("@remarka.minsk", true, "+375 29 1", true, "https://remarka.by", true, "09-21", true);

        candidate.Instagram.Should().Be("https://www.instagram.com/remarka.minsk/");
        candidate.Phone.Should().Be("+375 29 1");
        candidate.Website.Should().Be("https://remarka.by");
        candidate.OpeningHours.Should().Be("09-21");
    }

    [Fact]
    public void PatchContacts_EmptyString_ClearsField()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Cafe"), Now);
        candidate.PatchContacts("https://instagram.com/a", true, "+375", true, "https://a.by", true, "09-21", true);

        candidate.PatchContacts("", true, "", true, "", true, "", true);

        candidate.Instagram.Should().BeNull();
        candidate.Phone.Should().BeNull();
        candidate.Website.Should().BeNull();
        candidate.OpeningHours.Should().BeNull();
    }

    [Fact]
    public void PatchContacts_OmittedField_Stays()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Cafe"), Now);
        candidate.PatchContacts("@cafe", true, "+375", true, "https://a.by", true, "09-21", true);

        candidate.PatchContacts(null, false, null, false, null, false, "10-22", true);

        candidate.Instagram.Should().Be("https://www.instagram.com/cafe/");
        candidate.Phone.Should().Be("+375");
        candidate.OpeningHours.Should().Be("10-22");
    }

    [Fact]
    public void PatchContacts_PublishedToCatalog_Throws()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Cafe"), Now);
        candidate.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, [], Reviewer, false, Now);
        candidate.AttachPublishedShop(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        var act = () => candidate.PatchContacts("@x", true, null, false, null, false, null, false);

        act.Should().Throw<DomainException>().WithMessage("*published*");
    }

    [Fact]
    public void GetSuggestedTags_FromOsmSpecialtyName()
    {
        var tags = new Dictionary<string, string> { ["amenity"] = "cafe", ["name"] = "Roastery Brew Bar" };
        var snapshot = new OsmCandidateSnapshot(
            "node/1",
            "Roastery Brew Bar",
            "Немига 5, Минск",
            53.9152m,
            27.5847m,
            null, null, null, null, null, null,
            Now.AddMonths(-1),
            null,
            tags);
        var candidate = ShopImportCandidate.FromOsm(snapshot, Now);

        candidate.GetSuggestedTags().Should().Contain(t => t.Slug == "specialty");
        candidate.GetSuggestedFocus().Should().Be(ImportCoffeeFocus.Specialty);
    }

    [Fact]
    public void GetGaps_WithoutContacts_FlagsMissing()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Cafe"), Now);

        var gaps = candidate.GetGaps();
        gaps.Instagram.Should().BeTrue();
        gaps.Photo.Should().BeTrue();
    }







    [Fact]
    public void EnrichFrom_FillsMissingFieldsWithoutChangingStatus()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Coffe Joy"), Now);
        candidate.Phone.Should().BeNull();

        var richer = new OsmCandidateSnapshot(
            "node/1",
            "Coffe Joy",
            "Немига 5, Минск",
            53.9152m,
            27.5847m,
            "+375 29 111-22-33",
            "https://coffejoy.by",
            "https://instagram.com/coffejoy",
            "Mo-Fr 08:00-20:00",
            "coffee_shop",
            null,
            Now.AddMonths(-1),
            null,
            new Dictionary<string, string>
            {
                ["amenity"] = "cafe",
                ["name"] = "Coffe Joy",
                ["cuisine"] = "coffee_shop"
            });

        var changed = candidate.EnrichFrom(richer, Now, "https://maps.google.com/?q=joy");

        changed.Should().BeTrue();
        candidate.QueueStatus.Should().Be(ImportQueueStatus.Pending);
        candidate.Phone.Should().Be("+375 29 111-22-33");
        candidate.Website.Should().Be("https://coffejoy.by");
        candidate.Instagram.Should().Contain("coffejoy");
        candidate.OpeningHours.Should().Be("Mo-Fr 08:00-20:00");
        candidate.GoogleMapsUri.Should().Be("https://maps.google.com/?q=joy");
        candidate.CollectorBucket.Should().Be(ImportCollectorBucket.Priority);
        candidate.Signals.Should().Contain("import:merged");
    }

    [Fact]
    public void EnrichFrom_DoesNotOverwriteExistingPhone()
    {
        var withPhone = new OsmCandidateSnapshot(
            "node/1",
            "Coffe Joy",
            "Немига 5, Минск",
            53.9152m,
            27.5847m,
            "+375 29 111-22-33",
            null,
            null,
            null,
            null,
            null,
            Now.AddMonths(-1),
            null,
            new Dictionary<string, string> { ["amenity"] = "cafe", ["name"] = "Coffe Joy" });
        var candidate = ShopImportCandidate.FromOsm(withPhone, Now);

        candidate.EnrichFrom(Snapshot("node/1", "Coffe Joy Other"), Now);

        candidate.Phone.Should().Be("+375 29 111-22-33");
        candidate.Name.Should().Be("Coffe Joy");
    }

    [Fact]
    public void FromPlace_FileSource_SetsPending()
    {
        var candidate = ShopImportCandidate.FromPlace(ImportSource.File, Snapshot("file:abc", "Kitchen"), Now);

        candidate.Source.Should().Be(ImportSource.File);
        candidate.ExternalId.Should().Be("file:abc");
        candidate.QueueStatus.Should().Be(ImportQueueStatus.Pending);
        candidate.ImportedFromFile.Should().BeTrue();
        candidate.Signals.Should().Contain("import:file");
    }

    [Fact]
    public void ToSnapshot_RoundTripsContactFields()
    {
        var candidate = ShopImportCandidate.FromPlace(ImportSource.File, Snapshot("file:abc", "Kitchen"), Now);
        var snapshot = candidate.ToSnapshot();

        snapshot.Name.Should().Be("Kitchen");
        snapshot.Address.Should().Be("Немига 5, Минск");
        snapshot.Latitude.Should().Be(53.9152m);
    }

    [Fact]
    public void IsSamePlaceAs_NearbySameName_True()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Coffe Joy"), Now);
        var other = Snapshot("file:xyz", "Coffe Joy");

        candidate.IsSamePlaceAs(other).Should().BeTrue();
    }

    private static OsmCandidateSnapshot Snapshot(
        string externalId,
        string? name,
        DateTimeOffset? osmUpdatedAt = null)
    {
        var tags = new Dictionary<string, string> { ["amenity"] = "cafe" };
        if (name is not null)
            tags["name"] = name;

        return new OsmCandidateSnapshot(
            externalId,
            name,
            "Немига 5, Минск",
            53.9152m,
            27.5847m,
            null,
            null,
            null,
            null,
            null,
            null,
            osmUpdatedAt ?? Now.AddMonths(-1),
            null,
            tags);
    }

    [Fact]
    public void AttachMenuPhotos_SetsPendingAndCapturedAt()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Cafe"), Now);
        var utc = new DateTime(2026, 8, 26, 18, 0, 0, DateTimeKind.Utc);

        candidate.AttachMenuPhotos(
            [("menu.jpg", "image/jpeg", "menus/a.jpg", 12)],
            utc);

        candidate.Menu.Should().NotBeNull();
        candidate.Menu!.ParseStatus.Should().Be((int)MenuDraftParseStatus.Pending);
        candidate.Menu.CapturedAtUtc.Should().Be(utc);
        candidate.Menu.Photos.Should().HaveCount(1);
    }

    [Fact]
    public void ApplyMenuParseResult_Failure_KeepsPhotosAndSetsFailed()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Cafe"), Now);
        candidate.AttachMenuPhotos(
            [("menu.jpg", "image/jpeg", "menus/a.jpg", 12)],
            DateTime.UtcNow);

        candidate.ApplyMenuParseResult(false, "boom", null, [], [], DateTime.UtcNow, candidate.Menu!.ParseRequestId);

        candidate.Menu!.ParseStatus.Should().Be((int)MenuDraftParseStatus.Failed);
        candidate.Menu.ParseError.Should().Be("boom");
        candidate.Menu.Photos.Should().HaveCount(1);
        candidate.Menu.Items.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceMenuItems_MarksManualAndCanSetAbsent()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Cafe"), Now);
        candidate.ReplaceMenuItems(
        [
            new MenuDraftItem
            {
                Slug = "cappuccino",
                Availability = (int)MenuDraftAvailability.Absent,
                Source = (int)MenuDraftItemSource.Manual
            }
        ], DateTime.UtcNow);

        candidate.Menu!.Items.Should().ContainSingle(i =>
            i.Slug == "cappuccino" && i.Availability == (int)MenuDraftAvailability.Absent);
    }

    [Fact]
    public void Decide_RejectWhilePublishInFlight_Throws()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Varka"), Now);
        candidate.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, [], Reviewer, false, Now);

        var act = () => candidate.Decide(
            ImportQueueStatus.Rejected, null, [], Reviewer, false, Now, ImportRejectReason.Invalid);

        act.Should().Throw<DomainException>();
        candidate.QueueStatus.Should().Be(ImportQueueStatus.Published);
    }

    [Fact]
    public void Decide_AnyStatusAfterShopAttached_Throws()
    {
        var candidate = ShopImportCandidate.FromOsm(Snapshot("node/1", "Varka"), Now);
        candidate.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, [], Reviewer, false, Now);
        candidate.AttachPublishedShop(Guid.NewGuid());

        var act = () => candidate.Decide(ImportQueueStatus.Skipped, null, [], Reviewer, false, Now);

        act.Should().Throw<DomainException>().WithMessage("Candidate is already published to the catalog.");
    }
}
