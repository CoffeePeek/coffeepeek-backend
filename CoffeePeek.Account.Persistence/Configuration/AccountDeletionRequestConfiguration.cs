using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public class AccountDeletionRequestConfiguration : IEntityTypeConfiguration<AccountDeletionRequest>
{
    public void Configure(EntityTypeBuilder<AccountDeletionRequest> builder)
    {
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ConfirmationTokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OtherReason).HasMaxLength(500);
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.Version).IsRowVersion();

        builder.HasIndex(x => x.ConfirmationTokenHash).IsUnique();
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1)")
            .HasDatabaseName("UX_AccountDeletionRequests_ActiveUser");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
