using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using Review = CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.Review;
using CheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class ShopsDbContext(DbContextOptions<ShopsDbContext> options, IOptions<PublicAddressOptions>? addressOptions = null) : DbContext(options)
{
    public bool PublicAddressesEnabled => addressOptions?.Value.Enabled == true;
    public DbSet<PublicAddressAudit> PublicAddressAudit { get; set; }
    public virtual DbSet<CoffeeShopPublicAddress> CoffeeShopPublicAddresses { get; set; }
    public virtual DbSet<RoasterPublicAddress> RoasterPublicAddresses { get; set; }
    public virtual DbSet<CityPublicAddress> CityPublicAddresses { get; set; }
    public virtual DbSet<CoffeeZonePublicAddress> CoffeeZonePublicAddresses { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        // Deletions always retire (even when disabled); allocation only when enabled: creation for shops,
        // roasters and cities, first publication for coffee zones.
        var ownerChanges = ChangeTracker.Entries().Any(e =>
            (e.Entity is CoffeeShop or Roaster or City or CoffeeZone && e.State == EntityState.Deleted) ||
            (PublicAddressesEnabled && e.Entity is CoffeeShop or Roaster or City && e.State == EntityState.Added) ||
            (PublicAddressesEnabled && PublicAddressWriter.IsNewlyPublished(e)));
        if (!ownerChanges) return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        // Intercepts every supported repository/UoW/Wolverine save, including menu imports' early save.
        return PublicAddressTransaction.RunAsync(this, async () =>
        {
            await PublicAddressWriter.PrepareOwnerChangesAsync(this, cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }, cancellationToken);
    }
    //category
    public virtual DbSet<BrewMethod> BrewMethods { get; set; }
    
    public virtual DbSet<CoffeeBean> CoffeeBeans { get; set; }
    
    public virtual DbSet<Review> Reviews { get; set; }
    public virtual DbSet<ReviewHelpfulVote> ReviewHelpfulVotes { get; set; }
    public virtual DbSet<CheckIn> CheckIns { get; set; }
    
    public virtual DbSet<Roaster> Roasters { get; set; }
    
    public virtual DbSet<CoffeeShop> Shops { get; set; }
    
    public virtual DbSet<ShopPhoto> ShopPhotos { get; set; }
    public virtual DbSet<AppliedShopChangeRequest> AppliedShopChangeRequests { get; set; }

    public virtual DbSet<RoasterPhoto> RoasterPhotos { get; set; }
    
    public virtual DbSet<City> Cities { get; set; }
    public virtual DbSet<CoffeeZone> CoffeeZones { get; set; }
    public virtual DbSet<CoffeeZoneMembershipOverride> CoffeeZoneMembershipOverrides { get; set; }

    public virtual DbSet<EquipmentCategory> EquipmentCategories { get; set; }
    public virtual DbSet<Equipment> Equipments { get; set; }

    public virtual DbSet<ShopTag> ShopTags { get; set; }
    public virtual DbSet<CoffeeShopTag> CoffeeShopTags { get; set; }
    public virtual DbSet<CoffeeDrinkDefinition> CoffeeDrinkDefinitions { get; set; }
    public virtual DbSet<ShopMenu> ShopMenus { get; set; }
    public virtual DbSet<AppDistributionSettings> AppDistributionSettings { get; set; }
    public virtual DbSet<AndroidRelease> AndroidReleases { get; set; }
    public virtual DbSet<AppDistributionAuditLog> AppDistributionAuditLogs { get; set; }
    public virtual DbSet<AppDownloadRedirectEvent> AppDownloadRedirectEvents { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfiguration(new PublicAddressAuditConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeShopPublicAddressConfiguration());
        modelBuilder.ApplyConfiguration(new RoasterPublicAddressConfiguration());
        modelBuilder.ApplyConfiguration(new CityPublicAddressConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeZonePublicAddressConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeShopConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeZoneConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeZoneMembershipOverrideConfiguration());
        modelBuilder.ApplyConfiguration(new ShopPhotoConfiguration());
        modelBuilder.ApplyConfiguration(new AppliedShopChangeRequestConfiguration());
        modelBuilder.ApplyConfiguration(new RoasterConfiguration());
        modelBuilder.ApplyConfiguration(new RoasterPhotoConfiguration());
        modelBuilder.ApplyConfiguration(new ShopTagConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeShopTagConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeDrinkDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new ShopMenuConfiguration());
        modelBuilder.ApplyConfiguration(new ShopMenuItemConfiguration());
        modelBuilder.ApplyConfiguration(new ShopMenuPhotoConfiguration());
        modelBuilder.ApplyConfiguration(new AppDistributionSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new AndroidReleaseConfiguration());
        modelBuilder.ApplyConfiguration(new AppDistributionAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new AppDownloadRedirectEventConfiguration());
        
        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.CoffeeShopId);
            entity.HasIndex(r => r.UserId);
            // Backstop for the read-then-insert upsert in ModerationReviewApprovedHandler.
            entity.HasIndex(r => r.ModerationReviewId)
                .IsUnique()
                .HasFilter("\"ModerationReviewId\" IS NOT NULL");
            entity.Property(r => r.CoffeeShopId).IsRequired();
            entity.Property(r => r.ModerationReviewId);

            entity.Property(r => r.Header)
                .IsRequired(false)
                .HasMaxLength(BusinessConstants.MaxReviewHeaderLength);
            entity.Property(r => r.Comment).HasMaxLength(BusinessConstants.MaxReviewCommentLength);
            entity.Property(r => r.UserName).IsRequired().HasMaxLength(30);
            entity.Property(r => r.HelpfulCount).HasDefaultValue(0);
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_Reviews_HelpfulCount_NonNegative",
                "\"HelpfulCount\" >= 0"));

            // Foreign key without navigation property (DDD aggregate separation)
            entity.HasOne<CoffeeShop>()
                .WithMany()
                .HasForeignKey(r => r.CoffeeShopId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.OwnsOne<Rating>(r => r.Rating);

            // The backing field name (_shopPhotos) doesn't match EF's convention for "Photos";
            // without this, Include/materialization would try to write into a ReadOnlyCollection.
            // Access mode/backing field are not part of the snapshot -> no migration.
            entity.Navigation(r => r.Photos)
                .HasField("_shopPhotos")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ReviewHelpfulVote>(entity =>
        {
            entity.HasKey(v => new { v.ReviewId, v.UserId });
            entity.HasIndex(v => new { v.UserId, v.ReviewId });
            entity.Property(v => v.CreatedAtUtc).IsRequired();

            entity.HasOne<Review>()
                .WithMany()
                .HasForeignKey(v => v.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<CheckIn>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.UserId);
            entity.HasIndex(c => c.ShopId);
            entity.HasIndex(c => c.ReviewId);
            entity.HasIndex(c => new { c.UserId, c.ShopId });
            
            entity.Property(c => c.ShopId).IsRequired();
            entity.Property(c => c.ReviewId).IsRequired(false);
                
            entity.Property(c => c.Note).HasMaxLength(BusinessConstants.MaxCheckInNoteLength);
            
            // Foreign keys without navigation properties (DDD aggregate separation)
            entity.HasOne<CoffeeShop>()
                .WithMany()
                .HasForeignKey(c => c.ShopId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasOne<Review>()
                .WithMany()
                .HasForeignKey(c => c.ReviewId)
                .OnDelete(DeleteBehavior.SetNull);
            
            entity.OwnsOne<Rating>(r => r.Rating);
        });

        modelBuilder.Entity<Equipment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CategoryId);

            entity.HasOne(e => e.Category)
                .WithMany()
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<EquipmentCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(BusinessConstants.MaxEquipmentCategoryNameLength);
        });
    }
}
