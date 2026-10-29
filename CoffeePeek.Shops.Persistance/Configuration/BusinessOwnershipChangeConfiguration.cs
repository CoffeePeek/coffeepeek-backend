using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class BusinessOwnershipChangeConfiguration : IEntityTypeConfiguration<BusinessOwnershipChange>
{
    public void Configure(EntityTypeBuilder<BusinessOwnershipChange> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reason).HasMaxLength(BusinessOwnershipClaim.MaxReasonLength);
        builder.HasIndex(x => new { x.BusinessType, x.BusinessId, x.CreatedAtUtc, x.Id });
        builder.HasIndex(x => x.ClaimId).IsUnique().HasFilter("\"ClaimId\" IS NOT NULL");
        builder.ToTable("BusinessOwnershipChanges", table =>
        {
            table.HasCheckConstraint("CK_BusinessOwnershipChanges_Type", "\"BusinessType\" IN (0, 1)");
            table.HasCheckConstraint("CK_BusinessOwnershipChanges_Owners", "\"PreviousOwnerUserId\" IS DISTINCT FROM \"NewOwnerUserId\"");
        });
    }
}
