using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public record ModerationShopContact
{
    public string? PhoneNumber { get; private set; }
    public string? InstagramLink { get; private set; }
    public string? Email { get; private set; }
    public string? SiteLink { get; private set; }

    private ModerationShopContact() { }

    public static ModerationShopContact Create(string? phoneNumber, string? instagramLink,
        string? email, string? siteLink)
    {
        if (instagramLink?.Length > BusinessConstants.MaxShopContactInstagramLinkLength)
            throw new DomainException(
                $"Instagram link cannot be longer than {BusinessConstants.MaxShopContactInstagramLinkLength} characters");

        if (email?.Length > BusinessConstants.MaxShopContactEmailLength)
            throw new DomainException(
                $"Email cannot be longer than {BusinessConstants.MaxShopContactEmailLength} characters");


        if (siteLink?.Length > BusinessConstants.MaxShopContactSiteLinkLength)
            throw new DomainException(
                $"Site link cannot be longer than {BusinessConstants.MaxShopContactSiteLinkLength} characters");


        if (phoneNumber?.Length > BusinessConstants.MaxShopContactPhoneNumberLength)
            throw new DomainException(
                $"Phone number cannot be longer than {BusinessConstants.MaxShopContactPhoneNumberLength} characters");

        return new ModerationShopContact
        {
            PhoneNumber = phoneNumber,
            InstagramLink = instagramLink,
            Email = email,
            SiteLink = siteLink,
        };
    }
}