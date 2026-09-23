using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Moderation.Persistence.Configuration;

public class ShopImportCandidateConfiguration : IEntityTypeConfiguration<ShopImportCandidate>
{
    public void Configure(EntityTypeBuilder<ShopImportCandidate> entity)
    {
        entity.ToTable("ShopImportCandidates");
        entity.HasKey(e => e.Id);

        // Optimistic concurrency on PostgreSQL xmin: two moderators deciding the same item at once must not both win.
        entity.Property<uint>("Version").IsRowVersion();

        entity.Property(e => e.ExternalId).HasMaxLength(64).IsRequired();
        entity.Property(e => e.Name).HasMaxLength(ShopImportCandidate.MaxNameLength);
        entity.Property(e => e.Address).HasMaxLength(ShopImportCandidate.MaxAddressLength);
        entity.Property(e => e.Phone).HasMaxLength(ShopImportCandidate.MaxPhoneLength);
        entity.Property(e => e.Website).HasMaxLength(ShopImportCandidate.MaxWebsiteLength);
        entity.Property(e => e.Instagram).HasMaxLength(ShopImportCandidate.MaxInstagramLength);
        entity.Property(e => e.OpeningHours).HasMaxLength(ShopImportCandidate.MaxOpeningHoursLength);
        entity.Property(e => e.Cuisine).HasMaxLength(ShopImportCandidate.MaxCuisineLength);
        entity.Property(e => e.Brand).HasMaxLength(ShopImportCandidate.MaxBrandLength);
        entity.Property(e => e.CheckDate).HasMaxLength(ShopImportCandidate.MaxCheckDateLength);
        entity.Property(e => e.GoogleMapsUri).HasMaxLength(ShopImportCandidate.MaxGoogleMapsUriLength);
        entity.Property(e => e.Latitude).HasPrecision(18, 10);
        entity.Property(e => e.Longitude).HasPrecision(18, 10);

        // Npgsql 10 maps List<string> + jsonb natively. Do not convert through string:
        // reading jsonb as System.String requires EnableDynamicJson and aborts the reader
        // when it is missing (Gateway then returns 502 for GET /api/admin/import/candidates).
        entity.PrimitiveCollection(e => e.Signals)
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(StringListComparer());

        entity.PrimitiveCollection(e => e.TagSlugs)
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(StringListComparer());

        entity.HasIndex(e => new { e.Source, e.ExternalId }).IsUnique();
        entity.HasIndex(e => e.QueueStatus);
        entity.HasIndex(e => e.CollectorBucket);
        entity.HasIndex(e => e.CoffeeFocus);
        entity.HasIndex(e => e.RejectReason);

        entity.OwnsOne(e => e.Menu, menu =>
        {
            menu.ToJson();
            menu.OwnsMany(m => m.Items);
            menu.OwnsMany(m => m.Photos);
            menu.OwnsMany(m => m.Unmatched);
        });
    }

    private static ValueComparer<List<string>> StringListComparer() =>
        new(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item != null ? item.GetHashCode() : 0)),
            v => v.ToList());
}
