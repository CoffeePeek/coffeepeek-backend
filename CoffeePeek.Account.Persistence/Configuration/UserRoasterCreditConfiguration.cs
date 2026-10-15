using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public sealed class UserRoasterCreditConfiguration : IEntityTypeConfiguration<UserRoasterCredit>
{
    public void Configure(EntityTypeBuilder<UserRoasterCredit> builder)
    {
        builder.ToTable("UserRoasterCredits");
        builder.HasKey(x => x.RoasterId);
        builder.HasIndex(x => x.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
