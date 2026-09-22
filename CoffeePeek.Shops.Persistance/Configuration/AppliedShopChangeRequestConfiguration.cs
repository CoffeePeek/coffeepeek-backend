using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class AppliedShopChangeRequestConfiguration : IEntityTypeConfiguration<AppliedShopChangeRequest>
{
    public void Configure(EntityTypeBuilder<AppliedShopChangeRequest> entity)
    {
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.ShopId);
        entity.Property(x => x.AppliedAtUtc).IsRequired();
    }
}
