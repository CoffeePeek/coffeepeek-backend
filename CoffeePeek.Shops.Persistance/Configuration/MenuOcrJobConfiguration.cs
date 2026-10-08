using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class MenuOcrJobConfiguration : IEntityTypeConfiguration<MenuOcrJob>
{
    public void Configure(EntityTypeBuilder<MenuOcrJob> builder)
    {
        builder.ToTable("MenuOcrJobs", table =>
        {
            table.HasCheckConstraint("CK_MenuOcrJobs_Status", "\"Status\" BETWEEN 0 AND 5");
            table.HasCheckConstraint("CK_MenuOcrJobs_SourceKind", "\"SourceKind\" BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_MenuOcrJobs_MenuRevision", "\"MenuRevision\" IS NULL OR \"MenuRevision\" >= 0");
        });
        builder.HasKey(job => job.Id);
        builder.Property(job => job.PhotoSetHash).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(job => job.InputPhotosJson).IsRequired().HasColumnType("jsonb");
        builder.Property(job => job.ResultJson).HasColumnType("jsonb");
        builder.Property(job => job.Error).HasMaxLength(BusinessConstants.MaxMenuParseErrorLength);
        builder.HasIndex(job => new { job.SourceKind, job.SourceId, job.RequestedAtUtc });
        builder.HasIndex(job => new { job.ShopMenuId, job.Status });
    }
}
