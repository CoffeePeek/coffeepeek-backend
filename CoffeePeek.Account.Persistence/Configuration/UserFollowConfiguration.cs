using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public sealed class UserFollowConfiguration : IEntityTypeConfiguration<UserFollow>
{
    public void Configure(EntityTypeBuilder<UserFollow> builder)
    {
        builder.ToTable("UserFollows", table => table.HasCheckConstraint("CK_UserFollows_NoSelfFollow", "\"FollowerId\" <> \"FollowedId\""));
        builder.HasKey(x => new { x.FollowerId, x.FollowedId });
        builder.HasIndex(x => new { x.FollowedId, x.CreatedAtUtc, x.FollowerId });
        builder.HasIndex(x => new { x.FollowerId, x.CreatedAtUtc, x.FollowedId });
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.FollowerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.FollowedId).OnDelete(DeleteBehavior.Cascade);
    }
}
