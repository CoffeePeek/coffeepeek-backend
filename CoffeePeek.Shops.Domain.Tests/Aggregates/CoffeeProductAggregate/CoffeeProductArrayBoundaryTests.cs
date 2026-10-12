using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeProductArrayBoundaryTests
{
    [Fact]
    public void ContentArraysCannotChangeProductWithoutDomainOperation()
    {
        var tastes = new[] { "chocolate" };
        var brews = new[] { "espresso" };
        var grinds = new[] { "whole" };
        var features = new[] { "washed" };
        var product = Product(tastes, brews, grinds, features);
        var version = product.Version;

        tastes[0] = "input-changed";
        brews[0] = "input-changed";
        grinds[0] = "input-changed";
        features[0] = "input-changed";
        product.TasteDescriptors[0] = "output-changed";
        product.BrewRecommendations[0] = "output-changed";
        product.GrindOptions[0] = "output-changed";
        product.Features[0] = "output-changed";

        Assert.Equal(["chocolate"], product.TasteDescriptors);
        Assert.Equal(["espresso"], product.BrewRecommendations);
        Assert.Equal(["whole"], product.GrindOptions);
        Assert.Equal(["washed"], product.Features);
        Assert.Equal(version, product.Version);
    }

    [Fact]
    public void ProtectedFieldArrayCannotBeClearedThroughGetter()
    {
        var product = Product(["chocolate"], ["espresso"], ["whole"], ["washed"]);
        product.ProtectFields([nameof(CoffeeProduct.Name)]);
        var version = product.Version;

        product.ProtectedFields[0] = nameof(CoffeeProduct.Description);

        Assert.True(product.IsProtected(nameof(CoffeeProduct.Name)));
        Assert.False(product.IsProtected(nameof(CoffeeProduct.Description)));
        Assert.Equal(version, product.Version);
    }

    [Fact]
    public void CountryAndSourceCollectionsCannotBeChangedThroughPublicView()
    {
        var product = Product(["chocolate"], ["espresso"], ["whole"], ["washed"]);
        product.ReplaceCountries(["CO"]);
        var countryVersion = product.Version;

        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<CoffeeProductCountry>)product.Countries).Add(new CoffeeProductCountry(product.Id, "ET")));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<CoffeeProductSource>)product.Sources).Add(
                new CoffeeProductSource(product, "source", "external", "https://example.test/coffee")));
        Assert.Equal(["CO"], product.Countries.Select(c => c.CountryCode));
        Assert.Empty(product.Sources);
        Assert.Equal(countryVersion, product.Version);
    }

    [Fact]
    public void ImportCannotReplaceProtectedOrUnknownCountries()
    {
        var product = Product([], [], [], []);
        product.ReplaceCountries(["CO"]);
        product.ProtectFields([nameof(CoffeeProduct.Countries)]);
        var protectedVersion = product.Version;

        product.ApplyImportedCountries(["ET"]);
        product.ApplyImportedCountries([]);

        Assert.Equal(["CO"], product.Countries.Select(c => c.CountryCode));
        Assert.Equal(protectedVersion, product.Version);
    }

    private static CoffeeProduct Product(string[] tastes, string[] brews, string[] grinds, string[] features) =>
        new(Guid.NewGuid(), new CoffeeContent("Coffee", null, "roasted_beans", "whole_beans", "unknown",
            null, null, null, null, null, tastes, brews, grinds, features));
}
