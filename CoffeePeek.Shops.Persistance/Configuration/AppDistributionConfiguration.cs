using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class AppDistributionSettingsConfiguration : IEntityTypeConfiguration<AppDistributionSettings>
{
    public void Configure(EntityTypeBuilder<AppDistributionSettings> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.AndroidGooglePlayUrl)
            .HasMaxLength(BusinessConstants.MaxDownloadUrlLength);

        builder.Property(s => s.IosAppStoreUrl)
            .HasMaxLength(BusinessConstants.MaxDownloadUrlLength);
    }
}

public class AndroidReleaseConfiguration : IEntityTypeConfiguration<AndroidRelease>
{
    public void Configure(EntityTypeBuilder<AndroidRelease> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.IsActive)
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.HasIndex(r => r.VersionCode);

        builder.Property(r => r.Version)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxAndroidReleaseVersionLength);

        builder.Property(r => r.FileUrl)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxDownloadUrlLength);

        builder.Property(r => r.FileName)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxAndroidReleaseFileNameLength);

        builder.Property(r => r.Sha256)
            .IsRequired()
            .HasMaxLength(BusinessConstants.Sha256HashLength);
    }
}

public class AppDistributionAuditLogConfiguration : IEntityTypeConfiguration<AppDistributionAuditLog>
{
    public void Configure(EntityTypeBuilder<AppDistributionAuditLog> builder)
    {
        builder.HasKey(l => l.Id);
        builder.HasIndex(l => l.CreatedAtUtc);
        builder.HasIndex(l => l.UserId);

        builder.Property(l => l.Action)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxAuditActionLength);

        builder.Property(l => l.OldValue)
            .HasMaxLength(BusinessConstants.MaxAuditValueLength);

        builder.Property(l => l.NewValue)
            .HasMaxLength(BusinessConstants.MaxAuditValueLength);
    }
}

public class AppDownloadRedirectEventConfiguration : IEntityTypeConfiguration<AppDownloadRedirectEvent>
{
    public void Configure(EntityTypeBuilder<AppDownloadRedirectEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.Channel);

        builder.Property(e => e.Channel)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxRedirectChannelLength);

        builder.Property(e => e.UserAgent)
            .HasMaxLength(BusinessConstants.MaxRedirectUserAgentLength);

        builder.Property(e => e.Referer)
            .HasMaxLength(BusinessConstants.MaxRedirectRefererLength);

        builder.Property(e => e.Country)
            .HasMaxLength(BusinessConstants.MaxRedirectCountryLength);
    }
}
