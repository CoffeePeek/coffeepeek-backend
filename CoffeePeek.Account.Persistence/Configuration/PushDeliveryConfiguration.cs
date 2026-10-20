using CoffeePeek.Account.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public sealed class PushDeliveryConfiguration : IEntityTypeConfiguration<PushDelivery>
{
    public void Configure(EntityTypeBuilder<PushDelivery> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.Outcome).HasMaxLength(40);
        b.HasIndex(x => new { x.FinishedAtUtc, x.NextAttemptAtUtc });
        b.HasIndex(x => new { x.NotificationId, x.DeviceId }).IsUnique();
        b.HasOne(x => x.Notification).WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<PushDevice>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}
