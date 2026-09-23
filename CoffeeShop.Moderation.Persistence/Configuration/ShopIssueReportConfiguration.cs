using CoffeePeek.Moderation.Domain;
using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Moderation.Persistence.Configuration;

public class ShopIssueReportConfiguration : IEntityTypeConfiguration<ShopIssueReport>
{
    public void Configure(EntityTypeBuilder<ShopIssueReport> entity)
    {
        entity.UsePropertyAccessMode(PropertyAccessMode.Field);
        entity.HasKey(x => x.Id);

        // Optimistic concurrency on PostgreSQL xmin: two moderators deciding the same item at once must not both win.
        entity.Property<uint>("Version").IsRowVersion();

        entity.HasIndex(x => x.ShopId);
        entity.HasIndex(x => x.ReportedByUserId);
        // Queue pages filter by status and order by newest; the composite also serves status-only filters.
        entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });

        entity.Property(x => x.Description).HasMaxLength(BusinessConstants.MaxShopIssueReportDescriptionLength);
    }
}
