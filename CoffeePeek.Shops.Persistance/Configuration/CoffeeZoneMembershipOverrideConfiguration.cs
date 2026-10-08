using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class CoffeeZoneMembershipOverrideConfiguration : IEntityTypeConfiguration<CoffeeZoneMembershipOverride>
{
    public void Configure(EntityTypeBuilder<CoffeeZoneMembershipOverride> builder)
    {
        builder.ToTable("CoffeeZoneMembershipOverrides", table =>
            table.HasCheckConstraint("CK_CoffeeZoneMembershipOverrides_Kind", "\"Kind\" BETWEEN 0 AND 2"));
        builder.HasKey(x => new { x.ZoneId, x.ShopId });
        builder.HasOne<CoffeeZone>().WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CoffeeShop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ShopId);
        builder.HasIndex(x => x.ShopId)
            .IsUnique()
            .HasFilter("\"Kind\" = 2")
            .HasDatabaseName("UX_CoffeeZoneMembershipOverrides_PrimaryShop");
    }
}
