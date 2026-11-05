using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class ShelfProfilePreferenceConfiguration : IEntityTypeConfiguration<ShelfProfilePreference>
{
    public void Configure(EntityTypeBuilder<ShelfProfilePreference> entity)
    {
        entity.ToTable("ShelfProfilePreferences");
        entity.HasKey(p => p.OwnerId);
        entity.Property(p => p.IsVisible).HasDefaultValue(true);
    }
}
