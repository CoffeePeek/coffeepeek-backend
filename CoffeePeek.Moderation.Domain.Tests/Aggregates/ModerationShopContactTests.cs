using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Aggregates;

public class ModerationShopContactTests
{
    private const string ValidPhoneNumber = "+1234567890";
    private const string ValidInstagramLink = "https://instagram.com/coffeepeek";
    private const string ValidEmail = "contact@coffeepeek.com";
    private const string ValidSiteLink = "https://coffeepeek.com";

    [Fact]
    public void Create_WithValidValues_Succeeds()
    {
        var contact = ModerationShopContact.Create(ValidPhoneNumber, ValidInstagramLink, ValidEmail, ValidSiteLink);

        contact.PhoneNumber.Should().Be(ValidPhoneNumber);
        contact.InstagramLink.Should().Be(ValidInstagramLink);
        contact.Email.Should().Be(ValidEmail);
        contact.SiteLink.Should().Be(ValidSiteLink);
    }

    [Fact]
    public void Create_WithAllNullValues_Succeeds()
    {
        var contact = ModerationShopContact.Create(null, null, null, null);

        contact.PhoneNumber.Should().BeNull();
        contact.InstagramLink.Should().BeNull();
        contact.Email.Should().BeNull();
        contact.SiteLink.Should().BeNull();
    }

    [Fact]
    public void Create_WithPhoneNumberExceedingMaxLength_ThrowsDomainException()
    {
        var phoneNumber = new string('1', BusinessConstants.MaxShopContactPhoneNumberLength + 1);

        var act = () => ModerationShopContact.Create(phoneNumber, ValidInstagramLink, ValidEmail, ValidSiteLink);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithInstagramLinkExceedingMaxLength_ThrowsDomainException()
    {
        var instagramLink = new string('a', BusinessConstants.MaxShopContactInstagramLinkLength + 1);

        var act = () => ModerationShopContact.Create(ValidPhoneNumber, instagramLink, ValidEmail, ValidSiteLink);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithEmailExceedingMaxLength_ThrowsDomainException()
    {
        var email = new string('a', BusinessConstants.MaxShopContactEmailLength + 1);

        var act = () => ModerationShopContact.Create(ValidPhoneNumber, ValidInstagramLink, email, ValidSiteLink);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithSiteLinkExceedingMaxLength_ThrowsDomainException()
    {
        var siteLink = new string('a', BusinessConstants.MaxShopContactSiteLinkLength + 1);

        var act = () => ModerationShopContact.Create(ValidPhoneNumber, ValidInstagramLink, ValidEmail, siteLink);

        act.Should().Throw<DomainException>();
    }
}
