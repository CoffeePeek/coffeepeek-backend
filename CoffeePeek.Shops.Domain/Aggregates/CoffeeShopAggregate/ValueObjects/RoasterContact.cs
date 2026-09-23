using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public record RoasterContact
{
    public string? InstagramLink { get; private set; }
    public string? SiteLink { get; private set; }

    private RoasterContact(string? instagramLink, string? siteLink)
    {
        InstagramLink = instagramLink;
        SiteLink = siteLink;
    }

    public static RoasterContact Create(string? instagramLink, string? siteLink)
    {
        if (instagramLink?.Length > BusinessConstants.MaxRoasterContactInstagramLinkLength)
            throw new DomainException(
                $"Instagram link cannot be longer than {BusinessConstants.MaxRoasterContactInstagramLinkLength} characters");

        if (siteLink?.Length > BusinessConstants.MaxRoasterContactSiteLinkLength)
            throw new DomainException(
                $"Site link cannot be longer than {BusinessConstants.MaxRoasterContactSiteLinkLength} characters");

        return new RoasterContact(instagramLink, siteLink);
    }
}
