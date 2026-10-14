using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CheckInAggregate;

public class HomeBrewMethodCodeTests
{
    [Theory]
    [InlineData("cold-brew")]
    [InlineData("cezve-turkish-coffee")]
    [InlineData("chemex")]
    [InlineData("pour_over")]
    [InlineData("batch_drip")]
    [InlineData("french_press")]
    public void CatalogSlugsAndHistoricalCodesAreValid(string code) => Assert.True(HomeBrewMethodCode.IsValid(code));

    [Theory]
    [InlineData("")]
    [InlineData("bad method")]
    [InlineData("unknown_method")]
    [InlineData("Cold-Brew")]
    [InlineData("../method")]
    public void MalformedCodesAreRejected(string code) => Assert.False(HomeBrewMethodCode.IsValid(code));

    [Fact]
    public void LengthMatchesPublicCatalogSlugLimit()
    {
        Assert.True(HomeBrewMethodCode.IsValid(new string('a', 100)));
        Assert.False(HomeBrewMethodCode.IsValid(new string('a', 101)));
    }
}