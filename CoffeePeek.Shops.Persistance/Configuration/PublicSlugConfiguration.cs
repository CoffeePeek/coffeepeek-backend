using CoffeePeek.Shared.Domain.PublicAddresses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

internal static class PublicSlugConfiguration
{
    public static void Configure<T>(EntityTypeBuilder<T> builder, string table, string column = "Slug")
        where T : class, IPublicSlugOwner
    {
        builder.Property(e => e.PublicSlug).HasColumnName(column).HasMaxLength(PublicSlug.MaxLength).UseCollation("C");
        builder.Property(e => e.SlugRevision).IsConcurrencyToken();
        builder.HasIndex(e => e.PublicSlug).IsUnique().HasDatabaseName($"IX_{table}_{column}");
        builder.ToTable(table, t => t.HasCheckConstraint($"CK_{table}_PublicSlug",
            $"(\"{column}\" IS NULL AND \"SlugRevision\" = 0) OR (\"{column}\" IS NOT NULL AND \"{column}\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"{column}\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)"));
    }
}
