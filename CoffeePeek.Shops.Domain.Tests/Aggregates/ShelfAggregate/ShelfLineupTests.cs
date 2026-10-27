using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.ShelfAggregate;

public class ShelfLineupTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
    private readonly Guid _owner = Guid.NewGuid();

    private ShelfBag Bag(string name) =>
        ShelfBag.Create(Guid.NewGuid(), _owner, new ShelfBagDetails(null, name, null, null, null, null), null, Now);

    private (ShelfLineup Lineup, ShelfBag[] Bags) Shelf(int count)
    {
        var lineup = new ShelfLineup(_owner, []);
        var bags = Enumerable.Range(0, count).Select(i => Bag($"bag {i}")).ToArray();
        foreach (var bag in bags.Reverse()) lineup.AddToFront(bag);
        return (lineup, bags);
    }

    private static void AssertContiguous(ShelfLineup lineup, params ShelfBag[] expected)
    {
        Assert.Equal(expected, lineup.Bags);
        for (var i = 0; i < expected.Length; i++) Assert.Equal(i, expected[i].Position);
    }

    [Fact]
    public void NewBagsGoToTheFrontAndOthersShiftBack()
    {
        var (lineup, bags) = Shelf(2);
        var added = Bag("new");
        lineup.AddToFront(added);
        AssertContiguous(lineup, added, bags[0], bags[1]);
    }

    [Fact]
    public void TheShelfHoldsAtMostTwoHundredBags()
    {
        var (lineup, _) = Shelf(ShelfLineup.Capacity);
        Assert.Throws<ConflictException>(lineup.EnsureRoom);
        Assert.Throws<ConflictException>(() => lineup.AddToFront(Bag("one too many")));
        Assert.Equal(ShelfLineup.Capacity, lineup.Bags.Count);
    }

    [Fact]
    public void ReorderAcceptsOnlyAPermutationOfTheCurrentShelf()
    {
        var (lineup, bags) = Shelf(3);
        lineup.Reorder([bags[2].Id, bags[0].Id, bags[1].Id]);
        AssertContiguous(lineup, bags[2], bags[0], bags[1]);

        Assert.Throws<ConflictException>(() => lineup.Reorder([bags[2].Id, bags[0].Id]));
        Assert.Throws<ConflictException>(() => lineup.Reorder([bags[2].Id, bags[0].Id, bags[0].Id]));
        Assert.Throws<ConflictException>(() => lineup.Reorder([bags[2].Id, bags[0].Id, bags[1].Id, Guid.NewGuid()]));
        Assert.Throws<ConflictException>(() => lineup.Reorder([bags[2].Id, bags[0].Id, Guid.NewGuid()]));
        Assert.Throws<ConflictException>(() => lineup.Reorder(null));
        AssertContiguous(lineup, bags[2], bags[0], bags[1]);
    }

    [Fact]
    public void FinishingArchivesTheBagAndClosesTheGap()
    {
        var (lineup, bags) = Shelf(3);
        lineup.Finish(bags[1], 4, true, Now.AddDays(10));

        AssertContiguous(lineup, bags[0], bags[2]);
        Assert.Equal(ShelfBagStatus.Finished, bags[1].Status);
        Assert.Null(bags[1].Position);
        Assert.Equal(Now.AddDays(10), bags[1].FinishedAtUtc);
        Assert.Equal(4, bags[1].Rating);
        Assert.True(bags[1].BuyAgain);
        Assert.Throws<ConflictException>(() => lineup.Finish(bags[1], null, null, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void FinishingRejectsAnOutOfRangeRatingWithoutChangingTheShelf(int rating)
    {
        var (lineup, bags) = Shelf(2);
        Assert.Throws<DomainException>(() => lineup.Finish(bags[0], rating, null, Now));
        AssertContiguous(lineup, bags[0], bags[1]);
        Assert.Equal(ShelfBagStatus.OnShelf, bags[0].Status);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(99, 2)]
    [InlineData(-5, 0)]
    public void RestoreClampsThePositionAndClearsTheVerdict(int? requested, int expected)
    {
        var (lineup, bags) = Shelf(3);
        lineup.Finish(bags[1], 5, false, Now);
        lineup.Restore(bags[1], requested, Now.AddMinutes(1));

        Assert.Equal(ShelfBagStatus.OnShelf, bags[1].Status);
        Assert.Equal(expected, bags[1].Position);
        Assert.Null(bags[1].FinishedAtUtc);
        Assert.Null(bags[1].Rating);
        Assert.Null(bags[1].BuyAgain);
        Assert.Equal(Enumerable.Range(0, 3), lineup.Bags.Select(b => b.Position!.Value));
    }

    [Fact]
    public void UndoRestoresTheFinishedBagToItsPreviousPosition()
    {
        var (lineup, bags) = Shelf(3);
        lineup.Finish(bags[1], null, null, Now);
        lineup.Restore(bags[1], 1, Now);
        AssertContiguous(lineup, bags[0], bags[1], bags[2]);
    }

    [Fact]
    public void RestoringAShelfBagOrIntoAFullShelfIsAConflict()
    {
        var (lineup, bags) = Shelf(ShelfLineup.Capacity);
        Assert.Throws<ConflictException>(() => lineup.Restore(bags[0], 0, Now));

        lineup.Finish(bags[0], null, null, Now);
        lineup.AddToFront(Bag("refill"));
        Assert.Throws<ConflictException>(() => lineup.Restore(bags[0], 0, Now));
        Assert.Equal(ShelfBagStatus.Finished, bags[0].Status);
    }

    [Fact]
    public void RemovingABagClosesTheGapAndArchivedBagsDoNotMoveTheShelf()
    {
        var (lineup, bags) = Shelf(3);
        lineup.Finish(bags[2], null, null, Now);
        lineup.Remove(bags[2]);
        lineup.Remove(bags[0]);
        AssertContiguous(lineup, bags[1]);
    }

    [Fact]
    public void LoadedPositionsAreNormalizedAndOtherOwnersAreRejected()
    {
        var (_, bags) = Shelf(3);
        var reloaded = new ShelfLineup(_owner, [bags[2], bags[0], bags[1]]);
        AssertContiguous(reloaded, bags[0], bags[1], bags[2]);

        var stranger = ShelfBag.Create(Guid.NewGuid(), Guid.NewGuid(), new ShelfBagDetails(null, "x", null, null, null, null), null, Now);
        Assert.Throws<DomainException>(() => reloaded.AddToFront(stranger));
        Assert.Throws<DomainException>(() => reloaded.Finish(stranger, null, null, Now));
    }
}
