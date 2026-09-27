using CoffeePeek.Shops.Persistance.PublicAddresses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class PublicAddressAuditConfiguration : IEntityTypeConfiguration<PublicAddressAudit>
{
    public void Configure(EntityTypeBuilder<PublicAddressAudit> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Slug).HasMaxLength(100);
        builder.Property(a => a.PreviousSlug).HasMaxLength(100);
        builder.Property(a => a.Action).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(a => new { a.Kind, a.OwnerId, a.Revision }).IsUnique();
        builder.ToTable("PublicAddressAudit", t =>
        {
            t.HasCheckConstraint("CK_PublicAddressAudit_Kind", "\"Kind\" IN (1, 2)");
            t.HasCheckConstraint("CK_PublicAddressAudit_Revision", "\"Revision\" > 0");
        });
    }
}
