using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public sealed class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> builder)
    {
        builder.ToTable("UserBlocks", t => t.HasCheckConstraint("CK_UserBlocks_NoSelfBlock", "\"BlockerId\" <> \"BlockedId\""));
        builder.HasKey(x => new { x.BlockerId, x.BlockedId });
        builder.HasIndex(x => new { x.BlockedId, x.BlockerId });
        builder.HasIndex(x => new { x.BlockerId, x.CreatedAtUtc, x.BlockedId });
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.BlockerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.BlockedId).OnDelete(DeleteBehavior.Cascade);
    }
}
