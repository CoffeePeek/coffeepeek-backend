using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class SavedCollectionConfiguration : IEntityTypeConfiguration<SavedCollection>
{
    public void Configure(EntityTypeBuilder<SavedCollection> entity)
    {
        entity.ToTable("SavedCollections");
        entity.HasKey(c => c.Id);
        entity.Ignore(c => c.Mode);
        entity.Property(c => c.Title).HasMaxLength(100).IsRequired();
        entity.Property(c => c.Visibility).HasMaxLength(20).HasDefaultValue("private").IsRequired();
        entity.Property(c => c.Description).HasMaxLength(1000);
        entity.Property(c => c.CoverStorageKey).HasMaxLength(255);
        entity.Property(c => c.CoverContentType).HasMaxLength(100);
        entity.Property(c => c.Revision).HasDefaultValue(1L).IsConcurrencyToken();
        entity.HasIndex(c => new { c.OwnerId, c.CreatedAtUtc });
        entity.ToTable(table => table.HasCheckConstraint("CK_SavedCollections_Visibility", "\"Visibility\" IN ('public', 'private')"));
        entity.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CollectionId).OnDelete(DeleteBehavior.Cascade);
    }
}
