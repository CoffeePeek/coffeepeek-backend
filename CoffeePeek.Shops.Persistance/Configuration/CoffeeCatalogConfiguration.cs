using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Configuration;

internal static class CoffeeCatalogConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var country = model.Entity<Country>();
        country.ToTable("Countries"); country.HasKey(c => c.Code);
        country.Property(c => c.Code).HasMaxLength(2);
        country.Property(c => c.NameRu).HasMaxLength(100);
        country.Property(c => c.NameEn).HasMaxLength(100);
        country.HasData(InitialCountries());

        var filterValue = model.Entity<CoffeeFilterValue>();
        filterValue.ToTable("CoffeeFilterValues"); filterValue.HasKey(v => v.Id);
        filterValue.Property(v => v.GroupCode).HasMaxLength(30);
        filterValue.Property(v => v.Code).HasMaxLength(100);
        filterValue.Property(v => v.Name).HasMaxLength(100);
        filterValue.Property(v => v.Description).HasMaxLength(2000);
        filterValue.HasIndex(v => new { v.GroupCode, v.Code }).IsUnique();
        filterValue.HasData(CoffeeClassificationSeed.Values());

        var favorite = model.Entity<CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate.DiscoveryFavorite>();
        favorite.ToTable("DiscoveryFavorites");
        favorite.HasKey(f => new { f.UserId, f.Kind, f.EntityId });
        favorite.Property(f => f.Kind).HasMaxLength(30);
        favorite.HasIndex(f => new { f.Kind, f.EntityId });

        var product = model.Entity<CoffeeProduct>();
        PublicSlugConfiguration.Configure(product, "CoffeeProducts");
        product.HasKey(p => p.Id);
        product.Property(p => p.Name).HasMaxLength(250);
        product.Property(p => p.Description).HasMaxLength(20000);
        product.Property(p => p.ProductKind).HasMaxLength(30);
        product.Property(p => p.ProductForm).HasMaxLength(30);
        product.Property(p => p.CompositionKind).HasMaxLength(30);
        product.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        product.Property(p => p.Version).IsConcurrencyToken();
        product.Property(p => p.DefaultBrewPurposes).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
        product.Property(p => p.ProcessingCodes).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
        product.Property(p => p.FermentationCodes).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
        product.Property(p => p.TasteGroupCodes).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
        product.Property(p => p.ClassificationProtectedFields).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
        product.Property(p => p.CaffeineCode).HasMaxLength(100);
        product.Property(p => p.RoastLevelCode).HasMaxLength(100);
        product.Property(p => p.AcidityCode).HasMaxLength(100);
        product.Property(p => p.CompositionCode).HasMaxLength(100);
        product.HasOne<Roaster>().WithMany().HasForeignKey(p => p.RoasterId).OnDelete(DeleteBehavior.Restrict);
        product.HasIndex(p => new { p.RoasterId, p.Status });
        product.HasMany(p => p.Countries).WithOne().HasForeignKey(c => c.CoffeeProductId).OnDelete(DeleteBehavior.Cascade);
        product.HasMany(p => p.Sources).WithOne(s => s.Product).HasForeignKey(s => s.CoffeeProductId).OnDelete(DeleteBehavior.Cascade);

        var origin = model.Entity<CoffeeProductCountry>();
        origin.ToTable("CoffeeProductCountries"); origin.HasKey(c => new { c.CoffeeProductId, c.CountryCode });
        origin.Property(c => c.CountryCode).HasMaxLength(2);
        origin.HasOne(c => c.Country).WithMany().HasForeignKey(c => c.CountryCode).OnDelete(DeleteBehavior.Restrict);
        origin.HasIndex(c => c.CountryCode);

        var source = model.Entity<CoffeeProductSource>();
        source.ToTable("CoffeeProductSources"); source.HasKey(s => s.Id);
        source.Property(s => s.SourceKey).HasMaxLength(255);
        source.Property(s => s.ExternalProductId).HasMaxLength(300);
        source.Property(s => s.SourceUrl).HasMaxLength(2048);
        source.Property(s => s.RawJson).HasColumnType("jsonb");
        source.HasIndex(s => new { s.SourceKey, s.ExternalProductId }).IsUnique();
        source.HasMany(s => s.Variants).WithOne(v => v.Source).HasForeignKey(v => v.CoffeeProductSourceId).OnDelete(DeleteBehavior.Cascade);
        source.HasMany(s => s.Photos).WithOne().HasForeignKey(p => p.CoffeeProductSourceId).OnDelete(DeleteBehavior.Cascade);

        var variant = model.Entity<CoffeeVariant>();
        variant.ToTable("CoffeeVariants", t =>
        {
            t.HasCheckConstraint("CK_CoffeeVariants_Price", "\"Price\" > 0");
            t.HasCheckConstraint("CK_CoffeeVariants_Weight", "\"WeightGrams\" IS NULL OR \"WeightGrams\" > 0");
        });
        variant.HasKey(v => v.Id);
        variant.Property(v => v.SourceKey).HasMaxLength(255);
        variant.Property(v => v.ExternalVariantId).HasMaxLength(300);
        variant.Property(v => v.Price).HasPrecision(18, 2);
        variant.Property(v => v.Currency).HasMaxLength(3);
        variant.Property(v => v.Availability).HasConversion<string>().HasMaxLength(20);
        variant.Property(v => v.AvailabilityScope).HasMaxLength(30);
        variant.Property(v => v.SourceAttributesJson).HasColumnType("jsonb");
        variant.Property(v => v.BrewPurposeCode).HasMaxLength(100);
        variant.Property(v => v.BrewPurposeProtected).HasDefaultValue(false);
        variant.HasIndex(v => new { v.SourceKey, v.ExternalVariantId }).IsUnique();

        var photo = model.Entity<CoffeeProductPhoto>();
        photo.ToTable("CoffeeProductPhotos"); photo.HasKey(p => p.Id);
        photo.Property(p => p.StorageKey).HasMaxLength(255);
        photo.Property(p => p.SourceUrl).HasMaxLength(2048);
        photo.Property(p => p.FileName).HasMaxLength(255);
        photo.Property(p => p.ContentType).HasMaxLength(100);
        photo.HasIndex(p => new { p.CoffeeProductSourceId, p.StorageKey }).IsUnique();

        var run = model.Entity<CoffeeImportRun>();
        run.ToTable("CoffeeImportRuns"); run.HasKey(r => r.Id);
        run.Property(r => r.SourceKey).HasMaxLength(255);
        run.Property(r => r.SnapshotId).HasMaxLength(64);
        run.Property(r => r.PayloadHash).HasMaxLength(64);
        run.Property(r => r.Status).HasMaxLength(20);
        run.Property(r => r.Error).HasMaxLength(2000);
        run.HasIndex(r => new { r.SourceKey, r.SnapshotId }).IsUnique();
        run.HasIndex(r => new { r.SourceKey, r.CollectedAtUtc });

        var tag = model.Entity<RoasterTag>();
        tag.ToTable("RoasterTags"); tag.HasKey(t => t.Id);
        tag.Property(t => t.Slug).HasMaxLength(100);
        tag.Property(t => t.Name).HasMaxLength(100);
        tag.Property(t => t.Description).HasMaxLength(2000);
        tag.HasIndex(t => t.Slug).IsUnique();
        tag.HasData(CoffeeClassificationSeed.RoasterTags());
        var assignment = model.Entity<RoasterTagAssignment>();
        assignment.ToTable("RoasterTagAssignments"); assignment.HasKey(t => new { t.RoasterId, t.TagId });
        assignment.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Restrict);
        assignment.HasOne<Roaster>().WithMany().HasForeignKey(t => t.RoasterId).OnDelete(DeleteBehavior.Cascade);
        assignment.HasIndex(t => t.TagId);
    }

    private static Country[] InitialCountries() =>
    [
        new("CO", "Колумбия", "Colombia"), new("ET", "Эфиопия", "Ethiopia"),
        new("BR", "Бразилия", "Brazil"), new("KE", "Кения", "Kenya"),
        new("RW", "Руанда", "Rwanda"), new("CR", "Коста-Рика", "Costa Rica"),
        new("PE", "Перу", "Peru"), new("UG", "Уганда", "Uganda"),
        new("GT", "Гватемала", "Guatemala"), new("ID", "Индонезия", "Indonesia"),
        new("HN", "Гондурас", "Honduras"), new("BI", "Бурунди", "Burundi"),
        new("TZ", "Танзания", "Tanzania"), new("BO", "Боливия", "Bolivia"),
        new("NI", "Никарагуа", "Nicaragua"), new("CN", "Китай", "China"),
        new("MX", "Мексика", "Mexico"), new("VN", "Вьетнам", "Vietnam"),
        new("IN", "Индия", "India"), new("VE", "Венесуэла", "Venezuela"),
        new("ZW", "Зимбабве", "Zimbabwe"), new("YE", "Йемен", "Yemen"),
        new("PG", "Папуа-Новая Гвинея", "Papua New Guinea"), new("ZM", "Замбия", "Zambia"),
        new("CM", "Камерун", "Cameroon"), new("CG", "Конго", "Congo"),
        new("LA", "Лаос", "Laos"), new("MW", "Малави", "Malawi"),
        new("PA", "Панама", "Panama"), new("SV", "Сальвадор", "El Salvador"),
        new("DO", "Доминиканская Республика", "Dominican Republic")
    ];
}
