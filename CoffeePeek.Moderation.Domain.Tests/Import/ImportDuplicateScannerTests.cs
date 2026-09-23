using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Domain.Import;
using CoffeePeek.Shared.Domain.Places;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Import;

public class ImportDuplicateScannerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-26T12:00:00Z");
    private static readonly Guid Reviewer = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Scan_FindsBelarusianAndRussianSurfCoffee()
    {
        var osm = ShopImportCandidate.FromOsm(
            Place("node/1", "Surf Coffee", "пр. Незалежнасці, 25, Мінск", 53.9045m, 27.5615m), Now);
        var file = ShopImportCandidate.FromPlace(
            ImportSource.File,
            Place("file:abc", "Surf Coffee", "проспект Независимости, 25, Минск", 53.9052m, 27.5624m),
            Now);

        var suggestions = ImportDuplicateScanner.Scan([osm, file], new HashSet<(Guid, Guid)>());

        suggestions.Should().ContainSingle();
        suggestions[0].LeftCandidateId.Should().Be(ShopImportDuplicateSuggestion.Order(osm.Id, file.Id).Left);
        suggestions[0].Status.Should().Be(ImportDuplicateStatus.Pending);
        suggestions[0].Score.Should().BeGreaterThanOrEqualTo(65);
    }

    [Fact]
    public void Scan_FindsPairRegardlessOfInputOrder_WithDistantCandidateBetween()
    {
        // Guards the latitude sweep: input is unsorted and a far-away candidate sits between the pair.
        var a = ShopImportCandidate.FromOsm(Place("node/1", "Surf Coffee", "Nemiga 5", 53.9046m, 27.5616m), Now);
        var far = ShopImportCandidate.FromOsm(Place("node/2", "Other", "Far away 1", 52.0m, 23.0m), Now);
        var b = ShopImportCandidate.FromPlace(
            ImportSource.File, Place("file:abc", "Surf Coffee", "Nemiga 5", 53.9045m, 27.5615m), Now);

        var suggestions = ImportDuplicateScanner.Scan([a, far, b], new HashSet<(Guid, Guid)>());

        suggestions.Should().ContainSingle()
            .Which.LeftCandidateId.Should().Be(ShopImportDuplicateSuggestion.Order(a.Id, b.Id).Left);
    }

    [Fact]
    public void Scan_SkipsAlreadyTrackedPair()
    {
        var a = ShopImportCandidate.FromOsm(Place("node/1", "Surf Coffee", "Немига 5", 53.9045m, 27.5615m), Now);
        var b = ShopImportCandidate.FromPlace(
            ImportSource.File, Place("file:abc", "Surf Coffee", "Немига 5", 53.9046m, 27.5616m), Now);
        var pair = ShopImportDuplicateSuggestion.Order(a.Id, b.Id);

        var suggestions = ImportDuplicateScanner.Scan([a, b], new HashSet<(Guid, Guid)> { pair });

        suggestions.Should().BeEmpty();
    }

    [Fact]
    public void PickKeeper_PrefersPublishedOsm()
    {
        var osm = ShopImportCandidate.FromOsm(Place("node/1", "Surf Coffee", "Немига 5", 53.9045m, 27.5615m), Now);
        var file = ShopImportCandidate.FromPlace(
            ImportSource.File, Place("file:abc", "Surf Coffee", "Немига 5", 53.9045m, 27.5615m), Now);
        osm.Decide(ImportQueueStatus.Published, ImportCoffeeFocus.Cafe, null, Reviewer, false, Now);

        ImportDuplicateScanner.PickKeeper(file, osm).Id.Should().Be(osm.Id);
    }

    [Fact]
    public void Suggestion_RejectThenConfirm_SecondFails()
    {
        var a = ShopImportCandidate.FromOsm(Place("node/1", "Surf Coffee", "Немига 5", 53.9045m, 27.5615m), Now);
        var b = ShopImportCandidate.FromPlace(
            ImportSource.File, Place("file:abc", "Surf Coffee", "Немига 5", 53.9046m, 27.5616m), Now);
        var hint = PlaceDuplicateSuggester.Evaluate(
            a.Name, a.Address, a.Latitude, a.Longitude,
            b.Name, b.Address, b.Latitude, b.Longitude)!.Value;
        var suggestion = ShopImportDuplicateSuggestion.Create(a.Id, b.Id, hint);

        suggestion.Reject(Reviewer, Now);
        suggestion.Status.Should().Be(ImportDuplicateStatus.Rejected);

        var act = () => suggestion.Confirm(Reviewer, Now);
        act.Should().Throw<CoffeePeek.Shared.Kernel.Exceptions.DomainException>();
    }

    private static OsmCandidateSnapshot Place(
        string externalId,
        string name,
        string address,
        decimal lat,
        decimal lon) =>
        new(
            externalId,
            name,
            address,
            lat,
            lon,
            null,
            null,
            null,
            null,
            null,
            null,
            Now.AddMonths(-1),
            null,
            new Dictionary<string, string> { ["amenity"] = "cafe", ["name"] = name });
}
