using System.Reflection;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeChildMutationTests
{
    [Fact]
    public void ChildStateHasNoPublicSettersOrEmptyConstructors()
    {
        foreach (var type in new[] { typeof(CoffeeProductSource), typeof(CoffeeVariant), typeof(CoffeeProductPhoto), typeof(CoffeeImportRun) })
        {
            Assert.DoesNotContain(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                p => p.SetMethod?.IsPublic == true);
            Assert.Null(type.GetConstructor(Type.EmptyTypes));
        }
    }

    [Fact]
    public void MetadataOfferAndPhotoUpdatesAdvanceRootVersion()
    {
        var source = Source();
        var variant = new CoffeeVariant(source, "variant", 250, 10, "BYN");
        var photo = Photo(); source.AttachVariant(variant); source.AttachPhoto(photo);
        var version = source.Product.Version;

        source.UpdateMetadata("updated", "https://example.test/updated", "Brand", "Seller", "{}", DateTime.UtcNow);
        Assert.Equal(++version, source.Product.Version);
        variant.UpdateOffer(500, 20, "RUB", CoffeeAvailability.InStock, "in_store", DateTime.UtcNow, "ground", "filter");
        Assert.Equal(++version, source.Product.Version);
        source.UpdatePhoto(photo, "https://example.test/updated.jpg", "updated.jpg", "image/png", 200, Guid.NewGuid(), 1);
        Assert.Equal(++version, source.Product.Version);
        Assert.Equal("updated", source.ExternalProductId);
        Assert.Equal(500, variant.WeightGrams);
        Assert.Equal("image/png", photo.ContentType);
    }

    [Theory]
    [InlineData(0, 10, "BYN")]
    [InlineData(250, 0, "BYN")]
    [InlineData(250, -1, "BYN")]
    [InlineData(250, 1.001, "BYN")]
    [InlineData(250, 10, "USD")]
    public void InvalidOfferDoesNotMutateAnyFieldOrVersion(int weight, double price, string currency)
    {
        var source = Source();
        var variant = new CoffeeVariant(source, "variant", 250, 10, "BYN"); source.AttachVariant(variant);
        var version = source.Product.Version;
        Assert.Throws<DomainException>(() => variant.UpdateOffer(weight, (decimal)price, currency,
            CoffeeAvailability.InStock, "online", DateTime.UtcNow));
        Assert.Equal(250, variant.WeightGrams); Assert.Equal(10, variant.Price); Assert.Equal("BYN", variant.Currency);
        Assert.Equal(version, source.Product.Version);
    }

    [Fact]
    public void ReparentingCannotChangeEitherAggregate()
    {
        var first = Source(); var second = Source();
        var variant = new CoffeeVariant(first, "variant", null, 10, "BYN");
        var photo = Photo(); first.AttachVariant(variant); first.AttachPhoto(photo);
        var firstVersion = first.Product.Version; var secondVersion = second.Product.Version;
        Assert.Throws<DomainException>(() => second.AttachVariant(variant));
        Assert.Throws<DomainException>(() => second.AttachPhoto(photo));
        Assert.Throws<DomainException>(() => second.UpdatePhoto(photo, photo.SourceUrl, photo.FileName,
            photo.ContentType, photo.SizeBytes, photo.MediaPhotoId, 0));
        Assert.Empty(second.Variants); Assert.Empty(second.Photos);
        Assert.Equal(firstVersion, first.Product.Version); Assert.Equal(secondVersion, second.Product.Version);
    }

    [Fact]
    public void ProtectedBrewPurposeSurvivesImportWithoutFalseVersionChange()
    {
        var source = Source(); var variant = new CoffeeVariant(source, "variant", null, 10, "BYN");
        source.AttachVariant(variant); var version = source.Product.Version;
        variant.SetBrewPurpose(null, true);
        Assert.Equal(++version, source.Product.Version);
        variant.SetBrewPurpose("espresso", false, fromImport: true);
        Assert.Null(variant.BrewPurposeCode); Assert.True(variant.BrewPurposeProtected);
        Assert.Equal(version, source.Product.Version);
    }

    [Fact]
    public void InvalidLateContentCollectionDoesNotChangeName()
    {
        var product = Source().Product; var version = product.Version;
        Assert.Throws<DomainException>(() => product.ApplyContent(new("Changed", null, "roasted_beans",
            "whole_beans", "unknown", null, null, null, null, null, null!, [], [], [])));
        Assert.Equal("Coffee", product.Name); Assert.Equal(version, product.Version);
    }

    [Theory]
    [InlineData("Unknown", "CO")]
    [InlineData("Name", "invalid")]
    public void InvalidLateManualEditDoesNotChangeContentCountriesOrVersion(string protectedField, string country)
    {
        var product = Source().Product; product.ReplaceCountries(["ET"]); var version = product.Version;
        Assert.Throws<DomainException>(() => product.ApplyManualEdit(new("Changed", null, "roasted_beans",
            "whole_beans", "unknown", null, null, null, null, null, [], [], [], []),
            [country], [protectedField], CoffeePublicationStatus.Published));
        Assert.Equal("Coffee", product.Name); Assert.Equal(CoffeePublicationStatus.Draft, product.Status);
        Assert.Equal(["ET"], product.Countries.Select(c => c.CountryCode));
        Assert.Empty(product.ProtectedFields); Assert.Equal(version, product.Version);
    }

    private static CoffeeProductSource Source()
    {
        var product = new CoffeeProduct(Guid.NewGuid(), new("Coffee", null, "roasted_beans", "whole_beans",
            "unknown", null, null, null, null, null, [], [], [], []));
        var source = new CoffeeProductSource(product, "example.test", "product", "https://example.test/coffee");
        product.AttachSource(source); return source;
    }

    private static CoffeeProductPhoto Photo() => new("coffee/photo.jpg", "https://example.test/photo.jpg",
        "photo.jpg", "image/jpeg", 100, Guid.NewGuid());
}
