using System.Text.Json;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shops.Application.Features.Menu.ParseMenuPhotos;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Application.Observability;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CoffeePeek.Shops.Persistance.Menu;

/// <summary>Each instance uses one short-lived DbContext; callers use a fresh DI scope for each phase.</summary>
public sealed class MenuOcrJobStore(ShopsDbContext db, IApplyShopMenuService applyMenu)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static readonly TimeSpan OperatorRecoveryMinimumAge = TimeSpan.FromMinutes(15);

    public Task<MenuOcrJob?> GetAsync(Guid jobId, CancellationToken ct) =>
        db.MenuOcrJobs.AsNoTracking().FirstOrDefaultAsync(job => job.Id == jobId, ct);

    public async Task<bool> TryStartCurrentAsync(Guid jobId, CancellationToken ct)
    {
        await using var transaction = await BeginTransactionAsync(ct);
        if (!await LockAsync("MenuOcrJobs", jobId, ct))
            return false;
        var job = await db.MenuOcrJobs.SingleAsync(j => j.Id == jobId, ct);
        if (job.Status != MenuOcrJobStatus.Pending)
            return false;
        if (job.SourceKind != MenuOcrSourceKind.PublishedShop ||
            job.ShopMenuId is not { } menuId ||
            !await LockAsync("ShopMenus", menuId, ct))
        {
            job.MarkStale(DateTime.UtcNow);
            await CommitAsync(transaction, ct);
            ShopsTelemetry.OcrJobFinished(job);
            return false;
        }

        var menu = await db.ShopMenus.Include(m => m.Photos).SingleAsync(m => m.Id == menuId, ct);
        if (!job.Matches(menu))
        {
            job.MarkStale(DateTime.UtcNow);
            await CommitAsync(transaction, ct);
            ShopsTelemetry.OcrJobFinished(job);
            return false;
        }

        job.Start(DateTime.UtcNow);
        await CommitAsync(transaction, ct);
        return true;
    }

    public async Task<bool> StoreResultAsync(Guid jobId, string resultJson, CancellationToken ct)
    {
        await using var transaction = await BeginTransactionAsync(ct);
        if (!await LockAsync("MenuOcrJobs", jobId, ct))
            return false;
        var job = await db.MenuOcrJobs.SingleAsync(j => j.Id == jobId, ct);
        if (job.Status == MenuOcrJobStatus.ResultStored)
            return job.ResultJson == resultJson;
        if (job.Status != MenuOcrJobStatus.Running)
            return false;

        job.StoreResult(resultJson, DateTime.UtcNow);
        await CommitAsync(transaction, ct);
        return true;
    }

    /// <summary>
    /// Operator-only recovery for a worker lost after the paid provider call may have started.
    /// It never retries OCR. A late worker cannot store a result after this transition.
    /// </summary>
    public async Task<MenuOcrJobStatus?> RecoverUnknownOutcomeAsync(Guid jobId, DateTime nowUtc, CancellationToken ct)
    {
        await using var transaction = await BeginTransactionAsync(ct);
        if (!await LockAsync("MenuOcrJobs", jobId, ct))
            return null;
        var job = await db.MenuOcrJobs.SingleAsync(j => j.Id == jobId, ct);
        if (job.Status != MenuOcrJobStatus.Running)
            throw new ConflictException("Only a running OCR job can be recovered.", statusCode: 409);
        if (job.StartedAtUtc is not { } started || nowUtc - started < OperatorRecoveryMinimumAge)
            throw new ConflictException("The OCR job is too recent for operator recovery.", statusCode: 409);

        if (job.SourceKind != MenuOcrSourceKind.PublishedShop ||
            job.ShopMenuId is not { } menuId ||
            !await LockAsync("ShopMenus", menuId, ct))
        {
            job.MarkStale(nowUtc);
        }
        else
        {
            var menu = await db.ShopMenus.Include(m => m.Photos).SingleAsync(m => m.Id == menuId, ct);
            if (job.Matches(menu))
            {
                job.MarkProviderOutcomeUnknown(nowUtc);
                menu.MarkParseFailed(MenuOcrJob.ProviderOutcomeUnknownError);
            }
            else
                job.MarkStale(nowUtc);
        }

        await CommitAsync(transaction, ct);
        ShopsTelemetry.OcrJobFinished(job);
        return job.Status;
    }

    public async Task ApplyStoredAsync(Guid jobId, Guid? requestedByUserId, CancellationToken ct)
    {
        await using var transaction = await BeginTransactionAsync(ct);
        if (!await LockAsync("MenuOcrJobs", jobId, ct))
            return;
        var job = await db.MenuOcrJobs.SingleAsync(j => j.Id == jobId, ct);
        if (job.Status != MenuOcrJobStatus.ResultStored)
            return;
        if (job.ShopMenuId is not { } menuId || !await LockAsync("ShopMenus", menuId, ct))
        {
            job.MarkStale(DateTime.UtcNow);
            await CommitAsync(transaction, ct);
            ShopsTelemetry.OcrJobFinished(job);
            return;
        }

        var menu = await db.ShopMenus.Include(m => m.Photos).Include(m => m.Items)
            .SingleAsync(m => m.Id == menuId, ct);
        if (!job.Matches(menu))
        {
            job.MarkStale(DateTime.UtcNow);
            await CommitAsync(transaction, ct);
            ShopsTelemetry.OcrJobFinished(job);
            return;
        }

        var result = JsonSerializer.Deserialize<ParseMenuPhotosResponse>(job.ResultJson!, JsonOptions)
                     ?? throw new InvalidOperationException($"Stored OCR result {jobId} is invalid.");
        if (result.Success)
        {
            var photos = JsonSerializer.Deserialize<MenuOcrPhotoSnapshot[]>(job.InputPhotosJson, JsonOptions)
                         ?? throw new InvalidOperationException($"Stored OCR input {jobId} is invalid.");
            await applyMenu.ApplyParseResultAsync(
                menu.CoffeeShopId,
                result.Items,
                result.Unmatched,
                result.SuggestedPriceRange,
                photos.Select(p => new ShopMenuPhotoSnapshot(
                    p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.MediaPhotoId)).ToArray(),
                job.ResultStoredAtUtc ?? DateTime.UtcNow,
                requestedByUserId,
                ct);
            job.MarkApplied(DateTime.UtcNow);
        }
        else
        {
            await applyMenu.MarkParseFailedAsync(menu.CoffeeShopId, result.Error ?? "Parse failed.", ct);
            job.MarkFailed(result.Error ?? "Parse failed.", DateTime.UtcNow);
        }

        await CommitAsync(transaction, ct);
        ShopsTelemetry.OcrJobFinished(job);
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken ct) =>
        db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

    private async Task<bool> LockAsync(string table, Guid id, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return table == "MenuOcrJobs"
                ? await db.MenuOcrJobs.AnyAsync(j => j.Id == id, ct)
                : await db.ShopMenus.AnyAsync(m => m.Id == id, ct);

        // The table name is selected from two fixed call sites, never from message data.
        var sql = $"SELECT \"Id\" AS \"Value\" FROM \"{table}\" WHERE \"Id\" = {{0}} FOR UPDATE";
        return (await db.Database.SqlQueryRaw<Guid>(sql, id).ToListAsync(ct)).Count == 1;
    }

    private async Task CommitAsync(IDbContextTransaction? transaction, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
    }
}
