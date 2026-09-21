using CoffeePeek.Account.Domain;
using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Persistence.Configuration;

public class AccountDbContext(DbContextOptions<AccountDbContext> options) : DbContext(options)
{
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<PhotoMetadata> Photos { get; set; }
    public DbSet<AccountDeletionRequest> AccountDeletionRequests { get; set; }

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