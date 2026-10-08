using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Aggregates;

public class RatingTests
{
    [Theory]
    [InlineData(0, 3, 3)]
    [InlineData(6, 3, 3)]
    [InlineData(3, 0, 3)]
    [InlineData(3, 6, 3)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 3, 6)]
    public void Create_OutOfRange_ThrowsDomainException(int place, int service, int coffee)
    {
        var act = () => Rating.Create(place, service, coffee);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_InRange_SetsPlaceServiceCoffeeAndAverage()
    {
        var rating = Rating.Create(3, 4, 5);

        rating.Place.Should().Be(3);
        rating.Service.Should().Be(4);
        rating.Coffee.Should().Be(5);
        rating.AverageRating.Should().Be((3m + 4m + 5m) / 3m);
    }
}
