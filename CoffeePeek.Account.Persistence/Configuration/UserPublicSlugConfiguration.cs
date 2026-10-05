using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Domain.PublicAddresses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public sealed class UserPublicSlugConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.PublicSlug).HasColumnName("Slug").HasMaxLength(PublicSlug.MaxLength).UseCollation("C");
        builder.Property(u => u.SlugRevision).IsConcurrencyToken();
        builder.HasIndex(u => u.PublicSlug).IsUnique().HasDatabaseName("IX_Users_Slug");
        builder.ToTable("Users", t => t.HasCheckConstraint("CK_Users_PublicSlug",
            "(\"Slug\" IS NULL AND \"SlugRevision\" = 0) OR (\"Slug\" IS NOT NULL AND \"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100 AND \"SlugRevision\" > 0)"));
    }
}
