using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Domain.Import;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests.Import;

public class ImportDossierAdvisorTests
{
    [Fact]
    public void SuggestTags_SpecialtyName_AddsSpecialty()
    {
        var tags = ImportDossierAdvisor.SuggestTags(["name:specialty-signal", "name:coffee"], null);

        tags.Should().ContainSingle(t => t.Slug == "specialty" && t.Why == "from name");
    }

    [Fact]
    public void SuggestTags_ToGoChain_AddsToGo()
    {
        var tags = ImportDossierAdvisor.SuggestTags(["name:to-go-chain"], null);

        tags.Should().ContainSingle(t => t.Slug == "to_go" && t.Why == "to-go chain");
    }

    [Fact]
    public void SuggestTags_BakeryCuisine_AddsBakery()
    {
        var tags = ImportDossierAdvisor.SuggestTags(["osm:amenity=cafe"], "bakery;coffee");

        tags.Should().ContainSingle(t => t.Slug == "bakery");
    }

    [Fact]
    public void SuggestTags_DeduplicatesSlug()
    {
        var tags = ImportDossierAdvisor.SuggestTags(["name:specialty-signal", "name:specialty-signal"], null);

        tags.Should().ContainSingle(t => t.Slug == "specialty");
    }

    [Fact]
    public void SuggestFocus_SpecialtySignal_IsSpecialty()
    {
        ImportDossierAdvisor.SuggestFocus(["name:specialty-signal"], ImportCollectorBucket.Priority)
            .Should().Be(ImportCoffeeFocus.Specialty);
    }

    [Fact]
    public void SuggestFocus_ToGoChain_IsCoffeeBar()
    {
        ImportDossierAdvisor.SuggestFocus(["name:to-go-chain"], ImportCollectorBucket.LikelyNoise)
            .Should().Be(ImportCoffeeFocus.CoffeeBar);
    }

    [Fact]
    public void SuggestFocus_CoffeeName_IsCafe()
    {
        ImportDossierAdvisor.SuggestFocus(["name:coffee"], ImportCollectorBucket.Priority)
            .Should().Be(ImportCoffeeFocus.Cafe);
    }

    [Fact]
    public void Gaps_MissingContacts_AreTrue()
    {
        var gaps = ImportDossierAdvisor.Gaps(null, null, null, "09-21");

        gaps.Instagram.Should().BeTrue();
        gaps.Phone.Should().BeTrue();
        gaps.Website.Should().BeTrue();
        gaps.Hours.Should().BeFalse();
        gaps.Photo.Should().BeTrue();
    }
}

public class ImportContactNormalizerTests
{
    [Theory]
    [InlineData("@remarka.minsk", "https://www.instagram.com/remarka.minsk/")]
    [InlineData("remarka.minsk", "https://www.instagram.com/remarka.minsk/")]
    [InlineData("https://instagram.com/remarka.minsk", "https://www.instagram.com/remarka.minsk/")]
    [InlineData("https://www.instagram.com/remarka.minsk/", "https://www.instagram.com/remarka.minsk/")]
    public void Instagram_NormalizesHandleAndUrl(string raw, string expected)
    {
        ImportContactNormalizer.Instagram(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Instagram_Blank_IsNull(string? raw)
    {
        ImportContactNormalizer.Instagram(raw).Should().BeNull();
    }

    [Theory]
    [InlineData("https://google.com/foo")]
    [InlineData("https://www.instagram.com/p/AbC123/")]
    [InlineData("https://www.instagram.com/explore/locations/1/")]
    [InlineData("not a url")]
    public void Instagram_Invalid_Throws(string raw)
    {
        var act = () => ImportContactNormalizer.Instagram(raw);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Website_RequiresHttp()
    {
        var act = () => ImportContactNormalizer.Website("ftp://x.com", 2048);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Website_AcceptsHttps()
    {
        ImportContactNormalizer.Website("https://sound.by", 2048).Should().Be("https://sound.by");
    }
}
