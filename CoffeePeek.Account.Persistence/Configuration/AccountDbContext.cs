using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Domain;
using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.PublicAddresses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Persistence.Configuration;

// Only the singleton IOptions<T> may be injected: Aspire pools this context in Development (research Pitfall 7).
// The parameter stays optional for AccountDbContextFactory and tests.
public class AccountDbContext(DbContextOptions<AccountDbContext> options, IOptions<PublicAddressOptions>? addressOptions = null)
    : DbContext(options)
{
    public bool PublicAddressesEnabled => addressOptions?.Value.Enabled == true;

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var users = ChangeTracker.Entries<User>().ToArray();
        // Update the current slug in the same transaction as its user.
        if (!users.Any(e => ProfileAddressWriter.NeedsClear(e) ||
                            (PublicAddressesEnabled && ProfileAddressWriter.NeedsAddress(e))))
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        return ProfileAddressTransaction.RunAsync(this,
            () => ProfileAddressWriter.SaveAsync(this, users, acceptAllChangesOnSuccess, cancellationToken),
            cancellationToken);
    }

    /// <summary>Plain EF save for the address writer (demotion flush and final save); never re-enters the hook.</summary>
    internal Task<int> SaveCoreAsync(bool acceptAllChangesOnSuccess, CancellationToken ct) =>
        base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);

    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<PhotoMetadata> Photos { get; set; }
    public DbSet<AccountDeletionRequest> AccountDeletionRequests { get; set; }
    public DbSet<UserBlock> UserBlocks { get; set; }
    public DbSet<UserFollow> UserFollows { get; set; }
    public DbSet<UserRoasterCredit> UserRoasterCredits { get; set; }
    public DbSet<ActivityScore> ActivityScores { get; set; }
    public DbSet<CoffeePeek.Account.Domain.Entities.Notifications.PushDevice> PushDevices { get; set; }
    public DbSet<CoffeePeek.Account.Domain.Entities.Notifications.NotificationPreferences> NotificationPreferences { get; set; }
    public DbSet<CoffeePeek.Account.Domain.Entities.Notifications.PushNotification> PushNotifications { get; set; }
    public DbSet<CoffeePeek.Account.Domain.Entities.Notifications.PushDelivery> PushDeliveries { get; set; }
    public DbSet<CoffeePeek.Account.Domain.Entities.Notifications.PushNotificationTemplate> PushNotificationTemplates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountDbContext).Assembly);

        modelBuilder.Entity<PhotoMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StorageKey).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.Property(x => x.Token).HasMaxLength(BusinessConstants.MaxRefreshTokenLength).IsRequired();
            entity.Property(x => x.DeviceName).HasMaxLength(BusinessConstants.MaxDeviceNameLength);
            entity.Property(x => x.IpAddress).HasMaxLength(BusinessConstants.MaxIpAddressLength);
        });
    }
}
