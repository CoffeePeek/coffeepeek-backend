using CoffeePeek.Account.Domain.Entities.PublicAddressAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Domain.PublicAddresses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

// Account-local copy of the Shops registry schema (D-05); no navigation so a tombstone stays unlinkable (D-03); UserPublicAddressPersistenceTests pins parity.
public sealed class UserPublicAddressConfiguration : IEntityTypeConfiguration<UserPublicAddress>
{
    private const string Table = "UserPublicAddresses";

    public void Configure(EntityTypeBuilder<UserPublicAddress> builder)
    {
        builder.HasBaseType((Type?)null);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Slug).IsRequired().HasMaxLength(PublicSlug.MaxLength).UseCollation("C");
        builder.Property(a => a.Revision).IsConcurrencyToken();
        builder.HasIndex(a => a.Slug).IsUnique();
        // At most one canonical address per user.
        builder.HasIndex(a => a.OwnerId).IsUnique().HasFilter("\"State\" = 1");
        builder.HasIndex(a => new { a.OwnerId, a.State });
        builder.ToTable(Table, t =>
        {
            t.HasCheckConstraint($"CK_{Table}_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
            t.HasCheckConstraint($"CK_{Table}_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
            t.HasCheckConstraint($"CK_{Table}_Revision", "\"Revision\" > 0");
            t.HasCheckConstraint($"CK_{Table}_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
        });
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
