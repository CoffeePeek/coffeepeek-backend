using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class CheckInPhotoConfiguration : IEntityTypeConfiguration<CheckInPhoto>
{
    public void Configure(EntityTypeBuilder<CheckInPhoto> builder)
    {
        builder.ToTable("CheckInPhotos");
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.SortIndex).HasDefaultValue(0);
        builder.HasIndex(p => new { p.CanonicalCheckInId, p.SortIndex });
        builder.HasIndex(p => p.StorageKey).IsUnique();
    }
}
