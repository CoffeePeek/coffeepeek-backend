namespace CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

public interface IAppDistributionRepository
{
    Task<AppDistributionSettings> GetSettingsOrDefaultAsync(CancellationToken ct = default);
    Task<AppDistributionSettings> GetOrCreateSettingsAsync(CancellationToken ct = default);
    Task<AndroidRelease?> GetActiveAndroidReleaseAsync(CancellationToken ct = default);
    Task<AndroidRelease?> GetAndroidReleaseByIdAsync(Guid id, CancellationToken ct = default);
    Task<AndroidRelease[]> GetAndroidReleasesAsync(CancellationToken ct = default);
    Task<AndroidRelease[]> GetPublishedAndroidReleasesAsync(CancellationToken ct = default);
    Task<bool> HasRedirectEventAsync(Guid eventId, CancellationToken ct = default);
    void AddAndroidRelease(AndroidRelease release);
    void AddAuditLog(AppDistributionAuditLog auditLog);
    void AddRedirectEvent(AppDownloadRedirectEvent redirectEvent);
}
