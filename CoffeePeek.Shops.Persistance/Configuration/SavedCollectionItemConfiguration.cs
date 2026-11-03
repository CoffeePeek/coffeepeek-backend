using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class SavedCollectionItemConfiguration : IEntityTypeConfiguration<SavedCollectionItem>
{
    public void Configure(EntityTypeBuilder<SavedCollectionItem> entity)
    {
        entity.ToTable("SavedCollectionItems", table =>
            table.HasCheckConstraint("CK_SavedCollectionItems_Kind", "\"Kind\" IN ('coffee_shop', 'roaster')"));
        entity.HasKey(i => new { i.CollectionId, i.Kind, i.EntityId });
        entity.HasIndex(i => new { i.CollectionId, i.Position });
        entity.Property(i => i.Kind).HasMaxLength(20).IsRequired();
    }
}
