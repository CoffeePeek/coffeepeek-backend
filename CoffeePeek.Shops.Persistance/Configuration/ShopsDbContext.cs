using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Application.Observability;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using CheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class ShopsDbContext(DbContextOptions<ShopsDbContext> options, IOptions<PublicAddressOptions>? addressOptions = null,
    IPublicAddressOwnerWriter? addressOwners = null) : DbContext(options)
{
    internal IPublicAddressOwnerWriter AddressOwners { get; } = addressOwners ?? new PublicAddressOwnerWriter();
    public DbSet<CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.CoffeeProduct> CoffeeProducts => Set<CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.CoffeeProduct>();
    public DbSet<CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.CoffeeFilterValue> CoffeeFilterValues => Set<CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.CoffeeFilterValue>();
    public DbSet<CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate.DiscoveryFavorite> DiscoveryFavorites => Set<CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate.DiscoveryFavorite>();
    public DbSet<CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.Country> Countries => Set<CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.Country>();
    public DbSet<CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.CoffeeProductSource> CoffeeProductSources => Set<CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.CoffeeProductSource>();
    public DbSet<CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.CoffeeImportRun> CoffeeImportRuns => Set<CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.CoffeeImportRun>();
    public DbSet<CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate.RoasterTag> RoasterTags => Set<CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate.RoasterTag>();
    public DbSet<CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate.RoasterTagAssignment> RoasterTagAssignments => Set<CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate.RoasterTagAssignment>();
    public bool PublicAddressesEnabled => addressOptions?.Value.Enabled == true;


    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        // A profile write must fail as a conflict on its original request. Wolverine's global
        // transient-DB policy retries raw EF concurrency errors with a fresh context, which
        // could otherwise turn a concurrent legacy PUT into a successful stale overwrite.
        var profileRevisionChanged = ChangeTracker.Entries<CoffeeShop>().Any(entry =>
            entry.State == EntityState.Modified &&
            entry.Property(nameof(CoffeeShop.ProfileRevision)).IsModified);
        var ownerChanges = PublicAddressesEnabled && ChangeTracker.Entries().Any(e =>
            (e.Entity is IPublicSlugOwner && e.State == EntityState.Added && e.Entity is not CoffeeZone) ||
            PublicAddressWriter.IsNewlyPublished(e));
        try
        {
            if (!ownerChanges)
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            // Intercepts every supported repository/UoW/Wolverine save, including menu imports' early save.
            return await PublicAddressTransaction.RunAsync(this, async () =>
            {
                await PublicAddressWriter.PrepareOwnerChangesAsync(this, cancellationToken);
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) when (profileRevisionChanged)
        {
            // Do not retain the EF exception as InnerException: the global Wolverine retry
            // predicate walks the exception chain and would retry the stale profile write.
            ShopsTelemetry.ProfileWriteConflict();
            throw new ConflictException("Shop profile changed; reload and retry.", statusCode: 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                   { SqlState: PostgresErrorCodes.UniqueViolation,
                     ConstraintName: "IX_ShopMenus_CoffeeShopId" })
        {
            ShopsTelemetry.MenuWriteConflict();
            // Two requests may both observe that a shop has no menu. The database keeps
            // one row; surface the losing create as a conflict rather than an HTTP 500.
            throw new ConflictException("Shop menu was created concurrently; reload and retry.", statusCode: 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                   { SqlState: PostgresErrorCodes.UniqueViolation,
                     ConstraintName: "IX_CoffeeDrinkDefinitions_Slug" })
        {
            // The slug pre-check is only advisory when two administrators create concurrently.
            // The database unique index remains authoritative at Wolverine's final save.
            throw new ConflictException("Drink slug already exists.", statusCode: 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                   { SqlState: PostgresErrorCodes.UniqueViolation,
                     ConstraintName: "PK_Roasters" or "PK_RoasterPhotos" })
        {
            throw new ConflictException("Roaster or photo was attached concurrently; reload and retry.", statusCode: 409);
        }
    }
    //category
    public virtual DbSet<BrewMethod> BrewMethods { get; set; }

    public virtual DbSet<CoffeeBean> CoffeeBeans { get; set; }

    public virtual DbSet<CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.ReviewReport> ReviewReports { get; set; }
    public virtual DbSet<CheckIn> CheckIns { get; set; }
    public DbSet<Domain.Aggregates.CheckInAggregate.CheckInPhoto> CheckInPhotos => Set<Domain.Aggregates.CheckInAggregate.CheckInPhoto>();
    public virtual DbSet<Domain.Aggregates.CheckInAggregate.CheckInHelpfulVote> CheckInHelpfulVotes { get; set; }

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
    public virtual DbSet<ShopEquipment> ShopEquipments { get; set; }

    public virtual DbSet<ShopTag> ShopTags { get; set; }
    public virtual DbSet<CoffeeShopTag> CoffeeShopTags { get; set; }
    public virtual DbSet<CoffeeDrinkDefinition> CoffeeDrinkDefinitions { get; set; }
    public virtual DbSet<ShopMenu> ShopMenus { get; set; }
    public virtual DbSet<MenuOcrJob> MenuOcrJobs { get; set; }
    public virtual DbSet<AppDistributionSettings> AppDistributionSettings { get; set; }
    public virtual DbSet<AndroidRelease> AndroidReleases { get; set; }
    public virtual DbSet<AppDistributionAuditLog> AppDistributionAuditLogs { get; set; }
    public virtual DbSet<AppDownloadRedirectEvent> AppDownloadRedirectEvents { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        CoffeeCatalogConfiguration.Configure(modelBuilder);
        modelBuilder.HasPostgresExtension("pg_trgm");
        PublicSlugConfiguration.Configure(modelBuilder.Entity<ShopTag>(), "ShopTags", "PublicSlug");
        PublicSlugConfiguration.Configure(modelBuilder.Entity<BrewMethod>(), "BrewMethods");
        PublicSlugConfiguration.Configure(modelBuilder.Entity<Equipment>(), "Equipments");
        PublicSlugConfiguration.Configure(modelBuilder.Entity<CoffeeBean>(), "CoffeeBeans");
        PublicSlugConfiguration.Configure(modelBuilder.Entity<CoffeeZone>(), "CoffeeZones");
        PublicSlugConfiguration.Configure(modelBuilder.Entity<City>(), "Cities");
        PublicSlugConfiguration.Configure(modelBuilder.Entity<Roaster>(), "Roasters");
        PublicSlugConfiguration.Configure(modelBuilder.Entity<CoffeeShop>(), "Shops");

        modelBuilder.ApplyConfiguration(new CoffeeShopConfiguration());
        modelBuilder.ApplyConfiguration(new ShopEquipmentConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeZoneConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeZoneMembershipOverrideConfiguration());
        modelBuilder.ApplyConfiguration(new ShopPhotoConfiguration());
        modelBuilder.ApplyConfiguration(new CheckInPhotoConfiguration());
        modelBuilder.ApplyConfiguration(new AppliedShopChangeRequestConfiguration());
        modelBuilder.ApplyConfiguration(new RoasterConfiguration());
        modelBuilder.ApplyConfiguration(new RoasterPhotoConfiguration());
        modelBuilder.ApplyConfiguration(new ShopTagConfiguration());
        modelBuilder.ApplyConfiguration(new ReviewReportConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeShopTagConfiguration());
        modelBuilder.ApplyConfiguration(new CoffeeDrinkDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new ShopMenuConfiguration());
        modelBuilder.ApplyConfiguration(new ShopMenuItemConfiguration());
        modelBuilder.ApplyConfiguration(new ShopMenuPhotoConfiguration());
        modelBuilder.ApplyConfiguration(new MenuOcrJobConfiguration());
        modelBuilder.Entity<ShopMenu>(menu =>
        {
            menu.Property(m => m.InputRevision).HasDefaultValue(0L).IsConcurrencyToken();
            menu.HasMany(m => m.OcrJobs)
                .WithOne()
                .HasForeignKey(job => job.ShopMenuId)
                .OnDelete(DeleteBehavior.Cascade);
            menu.Navigation(m => m.OcrJobs).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.ApplyConfiguration(new AppDistributionSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new AndroidReleaseConfiguration());
        modelBuilder.ApplyConfiguration(new AppDistributionAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new AppDownloadRedirectEventConfiguration());
        modelBuilder.Entity<Domain.Aggregates.CheckInAggregate.CheckInHelpfulVote>(entity =>
        {
            entity.HasKey(x => new { x.CheckInId, x.UserId });
            entity.HasIndex(x => new { x.UserId, x.CheckInId });
            entity.HasOne<CheckIn>().WithMany().HasForeignKey(x => x.CheckInId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CheckIn>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.UserId);
            entity.HasIndex(c => c.ShopId);
            entity.HasIndex(c => new { c.UserId, c.ShopId });

            entity.Property(c => c.Kind).HasDefaultValue(Domain.Aggregates.CheckInAggregate.CheckInKind.Shop);
            entity.HasIndex(c => new { c.UserId, c.Kind, c.PreparedOn, c.VisitedAt, c.Id });
            entity.HasIndex(c => new { c.UserId, c.Kind, c.CoffeeProductId });
            entity.Property(c => c.BrewMethodCode).HasMaxLength(20);
            entity.Property(c => c.BeanNameSnapshot).HasMaxLength(250);
            entity.Property(c => c.DoseGrams).HasPrecision(8, 2);

            entity.Property(c => c.Note).IsRequired().HasMaxLength(BusinessConstants.MaxCheckInNoteLength);
            entity.Property(c => c.Username).HasMaxLength(30).HasDefaultValue("");
            entity.Property(c => c.Visibility).HasDefaultValue(Domain.Aggregates.CheckInAggregate.CheckInVisibility.Private);
            entity.Property(c => c.ContentRevision).HasDefaultValue(1);
            entity.Property(c => c.ModerationState).HasDefaultValue(Domain.Aggregates.CheckInAggregate.CheckInModerationState.NotSubmitted);
            entity.Property(c => c.RowVersion).IsConcurrencyToken();
            entity.Ignore(c => c.IsPubliclyVisible);
            entity.HasIndex(c => new { c.Visibility, c.ModerationState, c.CreatedAtUtc, c.Id });
            entity.HasIndex(c => new { c.FirstPublishedAtUtc, c.Id }).IsDescending()
                .HasFilter("\"DeletedAtUtc\" IS NULL AND \"Visibility\" = 1 AND \"ModerationState\" = 2 AND \"ApprovedRevision\" = \"ContentRevision\" AND \"FirstPublishedAtUtc\" IS NOT NULL");
            entity.HasIndex(c => new { c.UserId, c.ShopId, c.VisitedAt, c.CreatedAtUtc, c.Id });
            entity.Property(c => c.DrinkSlug).HasMaxLength(50);
            entity.Property(c => c.CustomDrinkName).HasMaxLength(100);
            entity.Property(c => c.DrinkNameRu).HasMaxLength(100);
            entity.Property(c => c.DrinkNameEn).HasMaxLength(100);
            entity.HasMany(c => c.ShopPhotos).WithOne().HasForeignKey(p => p.CanonicalCheckInId);
            entity.Navigation(c => c.ShopPhotos).HasField("_shopPhotos")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.ToTable("CanonicalCheckIns", t => t.HasCheckConstraint("CK_CanonicalCheckIns_CompleteContent",
                "\"Note\" IS NOT NULL AND length(btrim(\"Note\")) <= 1000 AND " +
                "((\"Rating_Place\" IS NULL AND \"Rating_Service\" IS NULL AND \"Rating_Coffee\" IS NULL AND \"Rating_AverageRating\" IS NULL) OR " +
                "(\"Rating_Place\" IS NOT NULL AND \"Rating_Service\" IS NOT NULL AND \"Rating_Coffee\" IS NOT NULL AND \"Rating_AverageRating\" IS NOT NULL AND " +
                "\"Rating_Place\" BETWEEN 1 AND 5 AND \"Rating_Service\" BETWEEN 1 AND 5 AND \"Rating_Coffee\" BETWEEN 1 AND 5))"));
            entity.ToTable("CanonicalCheckIns", t => t.HasCheckConstraint("CK_CanonicalCheckIns_Visibility", "\"Visibility\" IN (0, 1)"));
            entity.ToTable("CanonicalCheckIns", t => t.HasCheckConstraint("CK_CanonicalCheckIns_KindShape",
                "(\"Kind\" = 0 AND \"ShopId\" IS NOT NULL AND \"PreparedOn\" IS NULL AND " +
                "\"BrewMethodCode\" IS NULL AND \"CoffeeProductId\" IS NULL AND \"BeanNameSnapshot\" IS NULL AND " +
                "\"DoseGrams\" IS NULL AND \"BrewDurationSeconds\" IS NULL AND \"TasteScore\" IS NULL) OR " +
                "(\"Kind\" = 1 AND \"ShopId\" IS NULL AND \"PreparedOn\" IS NOT NULL AND " +
                "\"Rating_Place\" IS NULL AND \"Rating_Service\" IS NULL AND \"Rating_Coffee\" IS NULL AND " +
                "\"Rating_AverageRating\" IS NULL AND \"Visibility\" = 0 AND \"ModerationState\" = 0 AND " +
                "\"ApprovedRevision\" IS NULL AND \"ModerationSubmissionId\" IS NULL AND " +
                "\"FirstPublishedAtUtc\" IS NULL AND \"HelpfulCount\" = 0)"));
            entity.ToTable("CanonicalCheckIns", t => t.HasCheckConstraint("CK_CanonicalCheckIns_HomeDetails",
                "(\"BrewMethodCode\" IS NULL OR \"BrewMethodCode\" IN ('espresso', 'pour_over', 'batch_drip', 'moka_pot', 'cezve', 'aeropress', 'french_press', 'other')) AND " +
                "(\"CoffeeProductId\" IS NULL OR \"BeanNameSnapshot\" IS NOT NULL AND length(btrim(\"BeanNameSnapshot\")) > 0) AND " +
                "(\"DoseGrams\" IS NULL OR \"DoseGrams\" > 0 AND \"DoseGrams\" <= 10000) AND " +
                "(\"BrewDurationSeconds\" IS NULL OR \"BrewDurationSeconds\" BETWEEN 1 AND 604800) AND " +
                "(\"TasteScore\" IS NULL OR \"TasteScore\" BETWEEN 1 AND 5)"));

            // Foreign keys without navigation properties (DDD aggregate separation)
            entity.HasOne<CoffeeShop>()
                .WithMany()
                .HasForeignKey(c => c.ShopId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CoffeeProduct>()
                .WithMany()
                .HasForeignKey(c => c.CoffeeProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.OwnsOne<Rating>(r => r.Rating);
            entity.Navigation(c => c.Rating).IsRequired(false);
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
