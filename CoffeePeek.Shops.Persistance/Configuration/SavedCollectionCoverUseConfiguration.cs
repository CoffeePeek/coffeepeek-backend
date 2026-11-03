using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class SavedCollectionCoverUseConfiguration : IEntityTypeConfiguration<SavedCollectionCoverUse>
{
    public void Configure(EntityTypeBuilder<SavedCollectionCoverUse> entity)
    {
        entity.ToTable("SavedCollectionCoverUses");
        entity.HasKey(p => p.PhotoId);
        entity.HasOne<SavedCollection>().WithMany().HasForeignKey(p => p.CollectionId).OnDelete(DeleteBehavior.Cascade);
    }
}
