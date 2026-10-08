using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class ShopEquipmentConfiguration : IEntityTypeConfiguration<ShopEquipment>
{
    public void Configure(EntityTypeBuilder<ShopEquipment> builder)
    {
        builder.ToTable("CoffeeShopEquipments");
        builder.HasKey(x => new { x.ShopId, x.EquipmentId });
        builder.Property(x => x.ShopId).HasColumnName("CoffeeShopId");
        builder.Property(x => x.IsPrimary).HasDefaultValue(false);

        builder.HasIndex(x => new { x.ShopId, x.CategoryId })
            .IsUnique()
            .HasFilter("\"IsPrimary\"")
            .HasDatabaseName("UX_CoffeeShopEquipments_PrimaryCategory");

        builder.HasOne<CoffeeShop>()
            .WithMany(s => s.ShopEquipments)
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Equipment)
            .WithMany()
            .HasForeignKey(x => new { x.EquipmentId, x.CategoryId })
            .HasPrincipalKey(e => new { e.Id, e.CategoryId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
