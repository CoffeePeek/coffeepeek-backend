using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class ShelfBagConfiguration : IEntityTypeConfiguration<ShelfBag>
{
    public void Configure(EntityTypeBuilder<ShelfBag> builder)
    {
        builder.ToTable("ShelfBags", t =>
        {
            t.HasCheckConstraint("CK_ShelfBags_StatusPosition",
                "(\"Status\" = 0 AND \"Position\" IS NOT NULL AND \"Position\" >= 0 AND \"FinishedAtUtc\" IS NULL) OR " +
                "(\"Status\" = 1 AND \"Position\" IS NULL AND \"FinishedAtUtc\" IS NOT NULL)");
            t.HasCheckConstraint("CK_ShelfBags_Rating", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_ShelfBags_Weight", "\"WeightGrams\" IS NULL OR \"WeightGrams\" BETWEEN 1 AND 10000");
            t.HasCheckConstraint("CK_ShelfBags_Grind", "\"Grind\" IS NULL OR \"Grind\" IN (0, 1)");
            t.HasCheckConstraint("CK_ShelfBags_PhotoOrBean", "\"PhotoId\" IS NOT NULL OR \"Name\" IS NOT NULL");
            t.HasCheckConstraint("CK_ShelfBags_Photo",
                "(\"PhotoId\" IS NULL) = (\"PhotoStorageKey\" IS NULL) AND (\"PhotoId\" IS NULL) = (\"PhotoContentType\" IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PhotoStorageKey).HasMaxLength(ShelfBag.MaxStorageKeyLength);
        builder.Property(x => x.PhotoContentType).HasMaxLength(ShelfBag.MaxContentTypeLength);
        builder.Property(x => x.Name).HasMaxLength(ShelfBag.MaxTextLength);
        builder.Property(x => x.RoasterName).HasMaxLength(ShelfBag.MaxTextLength);
        builder.HasIndex(x => new { x.UserId, x.Status, x.Position });
        builder.HasIndex(x => new { x.UserId, x.Status, x.FinishedAtUtc, x.Id });
        builder.HasIndex(x => x.PhotoId).IsUnique().HasFilter("\"PhotoId\" IS NOT NULL");
    }
}
