using System.Text.Json;
using CoffeePeek.Contract.Events.Photo;
using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Community;

public sealed class CheckInPhotoVariantProjection(ShopsDbContext db) : ICheckInPhotoVariantProjection
{
    public async Task ApplyAsync(CheckInPhotoVariantsReadyEvent message, CancellationToken ct)
    {
        if (!message.IsValid()) throw new ArgumentException("Invalid photo variant manifest.", nameof(message));
        var key = await db.CheckInPhotos.AsNoTracking()
            .Where(p => p.Id == message.PhotoId && p.CanonicalCheckInId == message.CheckInId)
            .Select(p => p.StorageKey).SingleOrDefaultAsync(ct);
        if (key is null || CheckInPhotoVariantIdentity.SourceVersion(message.PhotoId, key) != message.SourceVersion) return;
        var json = JsonSerializer.Serialize(message.Variants);
        // Atomic current-binding/deletion fence, including a source change between the two queries.
        // Immutable keys make duplicate/older deliveries harmless; no aggregate write or new revision.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "CheckInPhotoVariantManifests" ("PhotoId", "CheckInId", "SourceVersion", "RecipeVersion", "VariantsJson")
            SELECT p."Id", c."Id", {message.SourceVersion}, {message.RecipeVersion}, CAST({json} AS jsonb)
            FROM "CheckInPhotos" p JOIN "CanonicalCheckIns" c ON c."Id" = p."CanonicalCheckInId"
            WHERE p."Id" = {message.PhotoId} AND p."StorageKey" = {key}
              AND c."Id" = {message.CheckInId} AND c."UserId" = {message.OwnerId} AND c."DeletedAtUtc" IS NULL
            ON CONFLICT ("PhotoId", "SourceVersion", "RecipeVersion") DO NOTHING
            """, ct);
    }
}
