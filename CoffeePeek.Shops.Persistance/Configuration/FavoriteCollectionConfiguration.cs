using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class FavoriteCollectionConfiguration : IEntityTypeConfiguration<FavoriteCollection>
{
    public void Configure(EntityTypeBuilder<FavoriteCollection> entity)
    {
        entity.ToTable("FavoriteCollections");
        entity.HasKey(c => c.Id);
        entity.Property(c => c.Title).HasMaxLength(100).IsRequired();
        entity.HasIndex(c => new { c.OwnerId, c.CreatedAtUtc });
        entity.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CollectionId).OnDelete(DeleteBehavior.Cascade);
    }
}
