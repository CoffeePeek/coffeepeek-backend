using CoffeePeek.Account.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public sealed class PushNotificationTemplateConfiguration : IEntityTypeConfiguration<PushNotificationTemplate>
{
    public void Configure(EntityTypeBuilder<PushNotificationTemplate> b)
    {
        b.HasKey(x => x.Category);
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.Body).HasMaxLength(500);
    }
}
