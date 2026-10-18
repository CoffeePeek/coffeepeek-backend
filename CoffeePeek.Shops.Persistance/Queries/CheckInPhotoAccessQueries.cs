using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.Shared.Kernel.Observability;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Queries;

public sealed class CheckInPhotoAccessQueries(ShopsDbContext db, IAuthorVisibilityLookup visibility,
    ICheckInPhotoVisibilityCache variantVisibility)
    : ICheckInPhotoAccessQueries
{
    public async Task<CheckInPhotoAccess?> GetVariantAsync(Guid checkInId, Guid photoId, Guid? readerId, CancellationToken ct)
    {
        using var measurement = PhotoDeliveryTelemetry.Start("access", readerId.HasValue ? "authenticated" : "anonymous");
        var facts = await variantVisibility.GetAsync(readerId, visibility, ct);
        var result = await ReaderQuery(checkInId, photoId, readerId, facts.ExcludedUserIds).SingleOrDefaultAsync(ct);
        // Owner-only/pending/rejected/hidden variants keep the fresh Account check as well as no-store.
        if (result is { IsPubliclyVisible: false } && readerId.HasValue)
        {
            var current = await visibility.ExcludedUserIdsAsync(readerId, ct);
            if (current.Contains(result.OwnerId)) result = null;
        }
        measurement.Complete(result is null ? "denied" : "allowed");
        return result is null ? null : result with { PolicyObservation = facts.Observation };
    }

    public async Task<CheckInPhotoAccess?> GetAsync(Guid checkInId, Guid photoId, Guid? readerId, CancellationToken ct)
    {
        using var measurement = PhotoDeliveryTelemetry.Start("access", readerId.HasValue ? "authenticated" : "anonymous");
        var excluded = await visibility.ExcludedUserIdsAsync(readerId, ct);
        var result = await ReaderQuery(checkInId, photoId, readerId, excluded).SingleOrDefaultAsync(ct);
        measurement.Complete(result is null ? "denied" : "allowed");
        return result;
    }

    public async Task<CheckInPhotoAccess?> GetModerationAsync(Guid checkInId, Guid photoId, int revision, CancellationToken ct)
    {
        using var measurement = PhotoDeliveryTelemetry.Start("access", "moderator");
        var rows = db.CheckIns.Where(c => c.Id == checkInId && c.ContentRevision == revision
            && c.DeletedAtUtc == null && c.Visibility == CheckInVisibility.Public);
        var result = await Project(rows, photoId).SingleOrDefaultAsync(ct);
        measurement.Complete(result is null ? "denied" : "allowed");
        return result;
    }

    internal IQueryable<CheckInPhotoAccess> ReaderQuery(Guid checkInId, Guid photoId, Guid? readerId, Guid[] excluded)
    {
        var eligible = CheckInEligibility.Public(db);
        return Project(db.CheckIns.Where(c => c.Id == checkInId && c.DeletedAtUtc == null
            && !excluded.Contains(c.UserId) && (c.UserId == readerId || eligible.Any(p => p.Id == c.Id))), photoId);
    }

    // The same table contains gallery photos and the separate drink cutout.
    private IQueryable<CheckInPhotoAccess> Project(IQueryable<CheckIn> rows, Guid photoId) =>
        rows.AsNoTracking().Join(db.CheckInPhotos.Where(p => p.Id == photoId), c => c.Id, p => p.CanonicalCheckInId,
            (c, p) => new CheckInPhotoAccess(c.Id, c.UserId, p.Id, p.ContentType,
                (CoffeePeek.Contract.Enums.CheckInKind)c.Kind, c.ContentRevision)
            {
                IsPubliclyVisible = CheckInEligibility.Public(db).Any(visible => visible.Id == c.Id),
                SourceVersion = CheckInPhotoVariantIdentity.SourceVersion(p.Id, p.StorageKey)
            });
}
