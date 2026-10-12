using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Moderation.Persistence.Configuration;

public class ShopImportDuplicateSuggestionConfiguration : IEntityTypeConfiguration<ShopImportDuplicateSuggestion>
{
    public void Configure(EntityTypeBuilder<ShopImportDuplicateSuggestion> entity)
    {
        entity.ToTable("ShopImportDuplicateSuggestions");
        entity.HasKey(e => e.Id);
        entity.Property<uint>("Version").IsRowVersion();

        entity.PrimitiveCollection(e => e.Reasons)
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(StringListComparer());

        entity.HasIndex(e => new { e.LeftCandidateId, e.RightCandidateId }).IsUnique();
        entity.HasIndex(e => e.Status);
        entity.HasIndex(e => e.Score);
    }

    private static ValueComparer<List<string>> StringListComparer() =>
        new(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item != null ? item.GetHashCode() : 0)),
            v => v.ToList());
}
