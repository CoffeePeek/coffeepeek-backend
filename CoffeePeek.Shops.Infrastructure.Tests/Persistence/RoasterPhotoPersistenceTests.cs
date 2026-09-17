using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class RoasterPhotoPersistenceTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options;
        using var dbContext = new ShopsDbContext(options);
        return dbContext.Model;
    }

    [Fact]
    public void Id_IsAppAssigned_SoReplacePhotosInsertsInsteadOfUpdating()
    {
        // Regression: with store-generated keys EF marked replaced photos Modified → UPDATE 0 rows → 409.
        var idProperty = BuildModel().FindEntityType(typeof(RoasterPhoto))!.FindProperty(nameof(RoasterPhoto.Id))!;

        idProperty.ValueGenerated.Should().Be(ValueGenerated.Never);
    }

    [Fact]
    public void RoasterRelationship_IsRequiredAndCascades_SoReplacedPhotosAreDeleted()
    {
        var fk = BuildModel().FindEntityType(typeof(RoasterPhoto))!.GetForeignKeys().Single();

        fk.IsRequired.Should().BeTrue();
        fk.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    [Fact]
    public void Model_UsesMediaServiceCompatibleMetadataLimits()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options;

        using var dbContext = new ShopsDbContext(options);
        var entityType = dbContext.Model.FindEntityType(typeof(RoasterPhoto));

        entityType.Should().NotBeNull();
        entityType!.FindProperty(nameof(RoasterPhoto.FileName))!.GetMaxLength()
            .Should().Be(BusinessConstants.MaxRoasterPhotoFileNameLength);
        entityType.FindProperty(nameof(RoasterPhoto.ContentType))!.GetMaxLength()
            .Should().Be(BusinessConstants.MaxRoasterPhotoContentTypeLength);
        entityType.FindProperty(nameof(RoasterPhoto.StorageKey))!.GetMaxLength()
            .Should().Be(BusinessConstants.MaxRoasterPhotoStorageKeyLength);
    }
}
