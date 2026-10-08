using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;

public record Rating
{
    public int Place { get; private set; }
    public int Service { get; private set; }
    public int Coffee { get; private set; }

    public decimal AverageRating { get; private set; }

    private Rating() { }

    internal Rating(int place, int service, int coffee)
    {
        if (place is < BusinessConstants.MinReviewRate or > BusinessConstants.MaxReviewRate)
            throw new DomainException(
                $"{nameof(place)} must be between {BusinessConstants.MinReviewRate} and {BusinessConstants.MaxReviewRate}.");

        if (service is < BusinessConstants.MinReviewRate or > BusinessConstants.MaxReviewRate)
            throw new DomainException(
                $"{nameof(service)} must be between {BusinessConstants.MinReviewRate} and {BusinessConstants.MaxReviewRate}.");

        if (coffee is < BusinessConstants.MinReviewRate or > BusinessConstants.MaxReviewRate)
            throw new DomainException(
                $"{nameof(coffee)} must be between {BusinessConstants.MinReviewRate} and {BusinessConstants.MaxReviewRate}.");

        Place = place;
        Service = service;
        Coffee = coffee;
        AverageRating = (decimal)(place + service + coffee) / 3;
    }

    public static Rating Create(int place, int service, int coffee) => new(place, service, coffee);

}
