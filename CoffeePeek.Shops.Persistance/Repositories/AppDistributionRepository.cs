using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class AppDistributionRepository(ShopsDbContext dbContext) : IAppDistributionRepository
{
    public async Task<AppDistributionSettings> GetOrCreateSettingsAsync(CancellationToken ct = default)
    {
        var settings = await dbContext.AppDistributionSettings
            .FirstOrDefaultAsync(s => s.Id == AppDistributionSettings.SingletonId, ct);

        if (settings is not null)
            return settings;

        settings = AppDistributionSettings.CreateDefault();
        dbContext.AppDistributionSettings.Add(settings);

        return settings;
    }

    public Task<AndroidRelease?> GetActiveAndroidReleaseAsync(CancellationToken ct = default) =>
        dbContext.AndroidReleases
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.ReleasedAt)
            .FirstOrDefaultAsync(ct);

    public Task<AndroidRelease?> GetAndroidReleaseByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.AndroidReleases.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<AndroidRelease[]> GetAndroidReleasesAsync(CancellationToken ct = default) =>
        dbContext.AndroidReleases
            .AsNoTracking()
            .OrderByDescending(r => r.ReleasedAt)
            .ThenByDescending(r => r.VersionCode)
            .ToArrayAsync(ct);

    public Task<AndroidRelease[]> GetPublishedAndroidReleasesAsync(CancellationToken ct = default) =>
        dbContext.AndroidReleases
            .Where(r => r.IsActive)
            .ToArrayAsync(ct);

    public void AddAndroidRelease(AndroidRelease release) => dbContext.AndroidReleases.Add(release);

    public void AddAuditLog(AppDistributionAuditLog auditLog) => dbContext.AppDistributionAuditLogs.Add(auditLog);

    public void AddRedirectEvent(AppDownloadRedirectEvent redirectEvent) =>
        dbContext.AppDownloadRedirectEvents.Add(redirectEvent);
}
