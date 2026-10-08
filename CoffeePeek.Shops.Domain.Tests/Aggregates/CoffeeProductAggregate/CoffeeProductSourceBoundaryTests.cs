using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeProductSourceBoundaryTests
{
    [Fact]
    public void ChildCollectionsAndWarningsCannotBeChangedThroughPublicView()
    {
        var product = new CoffeeProduct(Guid.NewGuid(), new CoffeeContent("Coffee", null,
            "roasted_beans", "whole_beans", "unknown", null, null, null, null, null, [], [], [], []));
        var source = new CoffeeProductSource
        {
            Product = product,
            CoffeeProductId = product.Id,
            SourceKey = "source",
            ExternalProductId = "external",
            SourceUrl = "https://example.test/coffee"
        };
        product.AttachSource(source);
        source.SetReviewWarnings(["needs_review"]);
        var version = product.Version;

        source.ReviewWarnings[0] = "changed";
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<CoffeeVariant>)source.Variants).Add(new CoffeeVariant()));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<CoffeeProductPhoto>)source.Photos).Add(new CoffeeProductPhoto()));

        Assert.Equal(["needs_review"], source.ReviewWarnings);
        Assert.Empty(source.Variants);
        Assert.Empty(source.Photos);
        Assert.Equal(version, product.Version);
    }

    [Fact]
    public void ChildMutationsAdvanceProductVersionAndRejectDuplicateVariant()
    {
        var product = new CoffeeProduct(Guid.NewGuid(), new CoffeeContent("Coffee", null,
            "roasted_beans", "whole_beans", "unknown", null, null, null, null, null, [], [], [], []));
        var source = new CoffeeProductSource
        {
            Product = product, CoffeeProductId = product.Id, SourceKey = "source",
            ExternalProductId = "external", SourceUrl = "https://example.test/coffee"
        };
        product.AttachSource(source);
        var before = product.Version;

        source.AttachVariant(new CoffeeVariant { ExternalVariantId = "variant-1", SourceKey = "source" });
        source.AttachPhoto(new CoffeeProductPhoto { StorageKey = "photo-1" });
        source.SetReviewWarnings(["review"]);
        var removed = source.RemovePhotosExcept(new HashSet<string>());

        Assert.True(product.Version > before);
        Assert.Equal("variant-1", Assert.Single(source.Variants).ExternalVariantId);
        Assert.Single(removed);
        Assert.Empty(source.Photos);
        Assert.Throws<CoffeePeek.Shared.Kernel.Exceptions.DomainException>(() =>
            source.AttachVariant(new CoffeeVariant { ExternalVariantId = "variant-1", SourceKey = "source" }));
    }
}
