using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class BusinessOwnershipClaimConfiguration : IEntityTypeConfiguration<BusinessOwnershipClaim>
{
    public void Configure(EntityTypeBuilder<BusinessOwnershipClaim> builder)
    {
        builder.Property(x => x.EvidencePhotoIds).HasColumnType("uuid[]").HasDefaultValueSql("ARRAY[]::uuid[]");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.BusinessNameSnapshot).IsRequired().HasMaxLength(500);
        builder.Property(x => x.BusinessSlugSnapshot).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Contact).IsRequired().HasMaxLength(BusinessOwnershipClaim.MaxContactLength);
        builder.Property(x => x.Evidence).IsRequired().HasMaxLength(BusinessOwnershipClaim.MaxEvidenceLength);
        builder.Property(x => x.DecisionReason).HasMaxLength(BusinessOwnershipClaim.MaxReasonLength);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.BusinessType, x.BusinessId, x.ApplicantUserId }).IsUnique()
            .HasFilter("\"Status\" = 0").HasDatabaseName("IX_BusinessOwnershipClaims_PendingApplicant");
        builder.HasIndex(x => new { x.ApplicantUserId, x.CreatedAtUtc, x.Id });
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc, x.Id });
        builder.ToTable("BusinessOwnershipClaims", table =>
        {
            table.HasCheckConstraint("CK_BusinessOwnershipClaims_Type", "\"BusinessType\" IN (0, 1)");
            table.HasCheckConstraint("CK_BusinessOwnershipClaims_Content",
                "length(btrim(\"Contact\")) BETWEEN 1 AND 320 AND length(btrim(\"Evidence\")) BETWEEN 1 AND 4000");
            table.HasCheckConstraint("CK_BusinessOwnershipClaims_Decision",
                "(\"Status\" = 0 AND \"ReviewedByUserId\" IS NULL AND \"ReviewedAtUtc\" IS NULL AND \"DecisionReason\" IS NULL) OR " +
                "(\"Status\" IN (1, 2) AND \"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL AND " +
                "(\"Status\" = 1 OR (\"DecisionReason\" IS NOT NULL AND length(btrim(\"DecisionReason\")) > 0)))");
        });
    }
}
