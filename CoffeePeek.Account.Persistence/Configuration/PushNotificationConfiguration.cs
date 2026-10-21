using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public sealed class PushNotificationConfiguration : IEntityTypeConfiguration<PushNotification>
{
    public void Configure(EntityTypeBuilder<PushNotification> b)
    {
        b.HasKey(x => x.Id);
        b.Ignore(x => x.Type);
        b.HasIndex(x => new { x.UserId, x.CreatedAtUtc, x.Id });
        b.HasIndex(x => new { x.UserId, x.ReadAtUtc });
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.Body).HasMaxLength(500);
        b.Property(x => x.EntityType).HasMaxLength(40);
        b.Property(x => x.Status).HasMaxLength(40);
        b.HasIndex(x => x.ExpiresAtUtc);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
