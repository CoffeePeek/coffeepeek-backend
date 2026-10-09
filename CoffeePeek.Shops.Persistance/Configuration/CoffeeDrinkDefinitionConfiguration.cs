using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class CoffeeDrinkDefinitionConfiguration : IEntityTypeConfiguration<CoffeeDrinkDefinition>
{
    public void Configure(EntityTypeBuilder<CoffeeDrinkDefinition> builder)
    {
        builder.ToTable("CoffeeDrinkDefinitions");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Slug)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxCoffeeDrinkSlugLength);

        builder.Property(d => d.NameRu)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxCoffeeDrinkNameLength);

        builder.Property(d => d.NameEn)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxCoffeeDrinkNameLength);

        builder.Property(d => d.Aliases)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxCoffeeDrinkAliasesLength);

        builder.HasIndex(d => d.Slug).IsUnique();
        builder.HasIndex(d => d.SortOrder);
    }
}
