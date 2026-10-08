using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class ReviewReportConfiguration : IEntityTypeConfiguration<ReviewReport>
{
    public void Configure(EntityTypeBuilder<ReviewReport> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Text).IsRequired().HasMaxLength(ReviewReport.MaxTextLength);
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc, x.Id });
        builder.HasIndex(x => x.ReportedByUserId);
        // Preserve the original target identifier even when no canonical public check-in survives.
        builder.Property(x => x.CheckInId).HasColumnName("ReviewId").IsRequired();
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ReviewReports_Text", "length(btrim(\"Text\")) BETWEEN 1 AND 2000");
            t.HasCheckConstraint("CK_ReviewReports_Resolution", "(\"Status\" = 0 AND \"ResolvedByAdminId\" IS NULL AND \"ResolvedAtUtc\" IS NULL) OR (\"Status\" IN (1, 2) AND \"ResolvedByAdminId\" IS NOT NULL AND \"ResolvedAtUtc\" IS NOT NULL)");
        });
    }
}
