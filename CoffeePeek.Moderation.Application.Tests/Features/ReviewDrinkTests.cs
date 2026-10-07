using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Shared.Kernel.Drinks;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ReviewDrinkTests
{
    [Fact]
    public async Task Snapshot_StoresTrimmedCustomDrinkWithoutCatalogLookup()
    {
        var catalog = new Mock<IDrinkCatalog>(MockBehavior.Strict);
        var selection = await DrinkSelection.ResolveAsync("other", "  Эспрессо-тоник  ", catalog.Object,
            TestContext.Current.CancellationToken);

        var snapshot = CreateSnapshot(Guid.NewGuid(), 1, selection);

        snapshot.DrinkSlug.Should().Be("other");
        snapshot.CustomDrinkName.Should().Be("Эспрессо-тоник");
        snapshot.DrinkNameRu.Should().Be("Эспрессо-тоник");
        snapshot.DrinkNameEn.Should().Be("Эспрессо-тоник");
        catalog.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Snapshot_PreservesTrustedNamesAfterCatalogDeactivation()
    {
        var catalog = new Mock<IDrinkCatalog>();
        catalog.Setup(x => x.FindAsync("latte", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DrinkCatalogEntry("Латте", "Latte", true));
        var selected = await DrinkSelection.ResolveAsync("latte", null, catalog.Object,
            TestContext.Current.CancellationToken);
        catalog.Setup(x => x.FindAsync("latte", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DrinkCatalogEntry("Новое название", "Renamed", false));

        var snapshot = CreateSnapshot(Guid.NewGuid(), 1, selected);

        snapshot.DrinkNameRu.Should().Be("Латте");
        snapshot.DrinkNameEn.Should().Be("Latte");
        catalog.Verify(x => x.FindAsync("latte", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void NewRevision_CanClearDrinkWithoutMutatingEarlierSnapshot()
    {
        var checkInId = Guid.NewGuid();
        var original = CreateSnapshot(checkInId, 1, new("latte", null, "Латте", "Latte"));
        var next = CreateSnapshot(checkInId, 2, null);

        original.DrinkSlug.Should().Be("latte");
        original.DrinkNameRu.Should().Be("Латте");
        next.DrinkSlug.Should().BeNull();
        next.CustomDrinkName.Should().BeNull();
        next.DrinkNameRu.Should().BeNull();
        next.DrinkNameEn.Should().BeNull();
    }

    private static ModerationReview CreateSnapshot(Guid checkInId, int revision, DrinkSelection? drink) =>
        ModerationReview.CreateCheckInSnapshot(checkInId, revision, Guid.NewGuid(), Guid.NewGuid(),
            "visitor", "A sufficiently long check-in comment", 5, 5, 5, drink, DateTime.UtcNow, []);
}
