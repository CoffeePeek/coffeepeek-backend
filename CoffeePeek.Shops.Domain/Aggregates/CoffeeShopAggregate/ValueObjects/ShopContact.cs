using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public record ShopContact
{
    public string? InstagramLink { get; private set; }
    [EmailAddress] 
    public string? Email { get; private set; }
    public string? SiteLink { get; private set; }
    public string? PhoneNumber { get; private set; }

    private ShopContact(string? instagramLink, string? email, string? siteLink, string? phoneNumber)
    {
        InstagramLink = instagramLink;
        Email = email;
        SiteLink = siteLink;
        PhoneNumber = phoneNumber;
    }

    public static ShopContact Create(string? instagramLink, string? email, string? siteLink,
        string? phoneNumber)
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


        return new ShopContact(instagramLink, email, siteLink, phoneNumber);
    }
}