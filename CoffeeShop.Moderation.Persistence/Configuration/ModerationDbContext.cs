using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ModerationReview = CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate.ModerationReview;
using ModerationShop = CoffeePeek.Moderation.Domain.Aggregates.ModerationShop;

namespace CoffeeShop.Moderation.Persistence.Configuration;

public class ModerationDbContext(DbContextOptions<ModerationDbContext> options) : DbContext(options)
{
    public DbSet<ModerationShop> ModerationShops { get; set; }
    public DbSet<ModerationReview> ModerationReviews { get; set; }
    public DbSet<PhotoMetadata> ShopPhotos { get; set; }
    public DbSet<ModerationShopEquipment> ModerationShopEquipments { get; set; }
    public DbSet<ModerationCoffeeBeanShop> ModerationCoffeeBeanShops { get; set; }
    public DbSet<ModerationShopRoaster> ModerationRoasterShops { get; set; }
    public DbSet<ModerationShopBrewMethod> ModerationShopBrewMethods { get; set; }
    public DbSet<ModerationAuditLog> ModerationAuditLogs { get; set; }
    public DbSet<ShopImportCandidate> ShopImportCandidates { get; set; }
    public DbSet<ShopImportDuplicateSuggestion> ShopImportDuplicateSuggestions { get; set; }
    public DbSet<ShopIssueReport> ShopIssueReports { get; set; }
    public DbSet<ModerationRoaster> ModerationRoasters { get; set; }
    public DbSet<ModerationRoasterPhoto> ModerationRoasterPhotos { get; set; }
    public DbSet<ShopChangeRequest> ShopChangeRequests { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ModerationDbContext).Assembly);
        
        modelBuilder.Entity<PhotoMetadata>(entity =>
        {
            entity.UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasKey(e => e.Id);
            entity.HasOne<ModerationShop>()
                .WithMany(s => s.ShopPhotos)
                .HasForeignKey(e => e.ModerationShopId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<ModerationShopEquipment>(entity =>
        {
            entity.UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ShopId);
            entity.HasIndex(e => e.EquipmentId);
        });
        
        modelBuilder.Entity<ModerationCoffeeBeanShop>(entity =>
        {
            entity.UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ShopId);
            entity.HasIndex(e => e.CoffeeBeanId);
        });
        
        modelBuilder.Entity<ModerationShopRoaster>(entity =>
        {
            entity.UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ShopId);
            entity.HasIndex(e => e.RoasterId);
        });
        
        modelBuilder.Entity<ModerationShopBrewMethod>(entity =>
        {
            entity.UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ShopId);
            entity.HasIndex(e => e.BrewMethodId);
        });

        modelBuilder.Entity<ModerationRoasterPhoto>(entity =>
        {
            entity.UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasKey(e => e.Id);
            entity.HasOne<ModerationRoaster>()
                .WithMany(r => r.Photos)
                .HasForeignKey(e => e.ModerationRoasterId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
