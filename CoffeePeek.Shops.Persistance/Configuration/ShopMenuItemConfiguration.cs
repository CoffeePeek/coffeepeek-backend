using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class ShopMenuItemConfiguration : IEntityTypeConfiguration<ShopMenuItem>
{
    public void Configure(EntityTypeBuilder<ShopMenuItem> builder)
    {
        builder.ToTable("ShopMenuItems", table => table.HasCheckConstraint(
            "CK_ShopMenuItems_Values",
            "\"Availability\" IN (0, 1, 2) AND ((\"Availability\" = 1 AND \"Price\" IS NOT NULL AND \"Price\" > 0 AND \"VolumeMl\" IS NOT NULL AND \"VolumeMl\" > 0) OR (\"Availability\" <> 1 AND \"Price\" IS NULL AND \"VolumeMl\" IS NULL))"));
        builder.HasKey(i => i.Id);
        // Domain-created ids must remain INSERTs when added to an already tracked menu.
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.HasIndex(i => new { i.ShopMenuId, i.DrinkDefinitionId }).IsUnique();
        builder.Property(i => i.Price).HasPrecision(10, 2);
        builder.Property(i => i.CustomName).HasMaxLength(BusinessConstants.MaxCoffeeDrinkNameLength);

        builder.HasOne(i => i.DrinkDefinition)
            .WithMany()
            .HasForeignKey(i => i.DrinkDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
