using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class FavoriteCollectionItemConfiguration : IEntityTypeConfiguration<FavoriteCollectionItem>
{
    public void Configure(EntityTypeBuilder<FavoriteCollectionItem> entity)
    {
        entity.ToTable("FavoriteCollectionItems", table =>
            table.HasCheckConstraint("CK_FavoriteCollectionItems_Kind", "\"Kind\" IN ('coffee_shop', 'roaster')"));
        entity.HasKey(i => new { i.CollectionId, i.Position });
        entity.HasIndex(i => new { i.CollectionId, i.Kind, i.EntityId }).IsUnique();
        entity.Property(i => i.Kind).HasMaxLength(20).IsRequired();
    }
}
