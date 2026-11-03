using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class SavedCollectionPreferenceConfiguration : IEntityTypeConfiguration<SavedCollectionPreference>
{
    public void Configure(EntityTypeBuilder<SavedCollectionPreference> entity)
    {
        entity.ToTable("SavedCollectionPreferences");
        entity.HasKey(p => p.OwnerId);
        entity.HasOne<SavedCollection>().WithMany().HasForeignKey(p => p.CollectionId).OnDelete(DeleteBehavior.SetNull);
    }
}
