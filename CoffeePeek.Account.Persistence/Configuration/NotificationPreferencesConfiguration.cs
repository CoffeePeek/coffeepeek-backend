using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public sealed class NotificationPreferencesConfiguration : IEntityTypeConfiguration<NotificationPreferences>
{
    public void Configure(EntityTypeBuilder<NotificationPreferences> b)
    {
        b.HasKey(x => x.UserId);
        b.HasOne<User>().WithOne().HasForeignKey<NotificationPreferences>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.Inactivity, x.LastSeenAtUtc });
    }
}
