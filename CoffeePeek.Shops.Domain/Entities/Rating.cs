using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Entities;

public record Rating
{
    public int Place { get; private set; }
    public int Service { get; private set; }
    public int Coffee { get; private set; }
    public decimal AverageRating { get; private set; }
    
    private Rating() { }
    
    internal Rating(int place, int service, int coffee)
    {
        Place = place;
        Service = service;
        Coffee = coffee;
        AverageRating = (Place + Service + Coffee) / 3m;
    }

    public static Rating Create(int place, int service, int coffee)
    {
        if (coffee is < BusinessConstants.MinReviewRate or > BusinessConstants.MaxReviewRate)
            throw new DomainException(
                $"{nameof(coffee)} must be between {BusinessConstants.MinReviewRate} and {BusinessConstants.MaxReviewRate}.");

        if (place is < BusinessConstants.MinReviewRate or > BusinessConstants.MaxReviewRate)
            throw new DomainException(
                $"{nameof(place)} must be between {BusinessConstants.MinReviewRate} and {BusinessConstants.MaxReviewRate}.");

        if (service is < BusinessConstants.MinReviewRate or > BusinessConstants.MaxReviewRate)
            throw new DomainException(
                $"{nameof(service)} must be between {BusinessConstants.MinReviewRate} and {BusinessConstants.MaxReviewRate}.");

        return new Rating(place, service, coffee);
    }
}