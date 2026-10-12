using CoffeePeek.MediaService.Data;
using Microsoft.EntityFrameworkCore;
using System.Buffers.Binary;
using Npgsql;
using CoffeePeek.Contract.Dtos.ShopPhotos;

namespace CoffeePeek.MediaService.Repositories;

public class PhotoRepository(MediaDbContext dbContext) : IPhotoRepository
{
    private readonly DbSet<PhotoMetadata> _repository = dbContext.Photos;

    public void Add(PhotoMetadata metadata)
    {
        _repository.Add(metadata);
    }

    public void AddRange(IEnumerable<PhotoMetadata> photoMetadatas)
    {
        _repository.AddRange(photoMetadatas);
    }

    public Task<PhotoMetadata?> GetByIdAsync(Guid photoId, CancellationToken ct = default)
    {
        return _repository.FirstOrDefaultAsync(x => x.Id == photoId, ct);
    }

    public Task<List<PhotoMetadata>> GetByStorageKeysAsync(
        Guid ownerId,
        IReadOnlyCollection<string> storageKeys,
        CancellationToken ct = default)
    {
        return _repository
            .Where(x => x.OwnerId == ownerId && (storageKeys.Contains(x.StorageKey)
                || (x.OriginalStorageKey != null && storageKeys.Contains(x.OriginalStorageKey))))
            .ToListAsync(ct);
    }

    public async Task<bool> TryClaimAsync(Guid photoId, Guid ownerId, Guid checkInId, CancellationToken ct = default)
    {
        var expiry = DateTime.UtcNow.AddHours(-24);
        return await _repository.Where(p => p.Id == photoId && p.OwnerId == ownerId
                && p.ShopAttachmentId == null
                && ((p.CheckInId == null && p.Status == PhotoStatus.Pending && p.UploadedAt >= expiry)
                    || (p.CheckInId == checkInId && (p.Status == PhotoStatus.Pending || p.Status == PhotoStatus.Confirmed))))
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.CheckInId, checkInId)
                .SetProperty(p => p.ClaimedAtUtc, DateTime.UtcNow), ct) == 1;
    }

    public async Task<bool> TryClaimShopBatchAsync(IReadOnlyList<Guid> photoIds, Guid ownerId, Guid shopId,
        ShopPhotoAttachmentKind kind, CancellationToken ct = default)
    {
        var expiry = DateTime.UtcNow.AddHours(-24);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        foreach (var photoId in photoIds)
        {
            var affected = await _repository.Where(p => p.Id == photoId && p.OwnerId == ownerId
                && p.BucketType == Configuration.BucketType.Shop && p.CheckInId == null
                && ((p.ShopAttachmentId == null && p.Status == PhotoStatus.Pending && p.UploadedAt >= expiry)
                    || (p.ShopAttachmentId == shopId && p.ShopAttachmentKind == kind
                        && (p.Status == PhotoStatus.Pending || p.Status == PhotoStatus.Confirmed))))
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.ShopAttachmentId, shopId)
                    .SetProperty(p => p.ShopAttachmentKind, kind)
                    .SetProperty(p => p.ClaimedAtUtc, DateTime.UtcNow), ct);
            if (affected == 1) continue;
            await transaction.RollbackAsync(ct);
            return false;
        }
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<IAsyncDisposable> AcquireClaimLocksAsync(Guid ownerId, IReadOnlyCollection<string> storageKeys, CancellationToken ct = default)
    {
        // Keep one PostgreSQL session throughout lookup, durable reservation, storage work and final save.
        // Session locks preserve already committed reservations when a later step fails.
        await dbContext.Database.OpenConnectionAsync(ct);
        var lease = new ClaimLocks(dbContext);
        try
        {
            var ids = await _repository.AsNoTracking().Where(p => p.OwnerId == ownerId
                    && (storageKeys.Contains(p.StorageKey) || (p.OriginalStorageKey != null && storageKeys.Contains(p.OriginalStorageKey))))
                .Select(p => p.Id).ToArrayAsync(ct);
            var lockKeys = ids.Select(id => BinaryPrimitives.ReadInt64LittleEndian(id.ToByteArray())).Distinct().Order().ToArray();
            foreach (var lockKey in lockKeys)
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({lockKey})", ct);
            return lease;
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    public async Task<List<PhotoMetadata>> GetClaimPhotosAsync(Guid ownerId, IReadOnlyCollection<string> storageKeys, CancellationToken ct = default)
    {
        var photos = await GetByStorageKeysAsync(ownerId, storageKeys, ct);
        foreach (var photo in photos) await dbContext.Entry(photo).ReloadAsync(ct);
        return photos.Where(p => p.OwnerId == ownerId && (storageKeys.Contains(p.StorageKey)
            || (p.OriginalStorageKey != null && storageKeys.Contains(p.OriginalStorageKey)))).ToList();
    }

    private sealed class ClaimLocks(MediaDbContext context) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock_all()", CancellationToken.None); }
            catch
            {
                // Never return a session with an ambiguous lock acquisition/unlock to the pool.
                if (context.Database.GetDbConnection() is NpgsqlConnection connection) NpgsqlConnection.ClearPool(connection);
                throw;
            }
            finally { await context.Database.CloseConnectionAsync(); }
        }
    }

    public Task ReleaseClaimAsync(Guid ownerId, Guid checkInId, CancellationToken ct = default) =>
        _repository.Where(p => p.OwnerId == ownerId && p.CheckInId == checkInId && p.Status == PhotoStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.CheckInId, (Guid?)null)
                .SetProperty(p => p.ClaimedAtUtc, (DateTime?)null), ct);

    public Task<PhotoMetadata?> GetCheckInPhotoAsync(Guid checkInId, Guid photoId, Guid ownerId, CancellationToken ct = default) =>
        _repository.AsNoTracking().FirstOrDefaultAsync(p => p.Id == photoId && p.CheckInId == checkInId
            && p.OwnerId == ownerId && p.BucketType == Configuration.BucketType.CheckIn
            && (p.Status == PhotoStatus.Pending || p.Status == PhotoStatus.Confirmed), ct);
}
