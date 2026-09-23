using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public record ModerationRoasterContact
{
    public string? InstagramLink { get; private set; }
    public string? SiteLink { get; private set; }

    private ModerationRoasterContact() { }

    public static ModerationRoasterContact Create(string? instagramLink, string? siteLink)
    {
        if (instagramLink?.Length > BusinessConstants.MaxShopContactInstagramLinkLength)
            throw new DomainException(
                $"Instagram link cannot be longer than {BusinessConstants.MaxShopContactInstagramLinkLength} characters");

        if (siteLink?.Length > BusinessConstants.MaxShopContactSiteLinkLength)
            throw new DomainException(
                $"Site link cannot be longer than {BusinessConstants.MaxShopContactSiteLinkLength} characters");

        return new ModerationRoasterContact
        {
            InstagramLink = instagramLink,
            SiteLink = siteLink
        };
    }
}
