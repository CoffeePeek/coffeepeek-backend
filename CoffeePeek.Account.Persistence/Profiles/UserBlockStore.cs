using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Contract.Dtos.Profiles;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Persistence.Profiles;

public sealed class UserBlockStore(AccountDbContext db, IOptions<MediaPublicUrlOptions> mediaOptions) : IUserBlockStore
{
    public async Task<AuthorBlockPageDto> ListAsync(Guid viewerId, int pageSize, string? cursor, CancellationToken ct)
    {
        if (pageSize is < 1 or > 100) throw new ValidationException("PageSize must be between 1 and 100.");
        var items = await ReadAuthorsAsync(viewerId, ct);
        var revision = Revision(items);
        var offset = 0;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length > 180) throw new FormatException();
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
                if (parts.Length != 2 || parts[0] != revision ||
                    !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out offset) ||
                    offset < 1 || offset >= items.Length) throw new FormatException();
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            { throw new ValidationException("Block list changed. Reload from the first page."); }
        }
        var page = items.Skip(offset).Take(pageSize).ToArray();
        var next = offset + page.Length < items.Length
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"{revision}:{offset + page.Length}")) : null;
        return new(page, next, revision);
    }

    public async Task<AuthorBlockStateDto> SetAsync(Guid viewerId, Guid publicAuthorId, bool blocked, CancellationToken ct)
    {
        var author = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.PublicAuthorId == publicAuthorId, ct);
        if (author is null) throw new NotFoundException("Author not found.");
        if (author.Id == viewerId) throw new ValidationException("You cannot block yourself.");
        if (!await db.Users.AnyAsync(u => u.Id == viewerId && !u.IsBlocked && !u.IsSoftDelete, ct))
            throw new NotFoundException("User not found.");
        // Removing a block remains possible after the author's account is deactivated.
        if (blocked && (author.IsBlocked || author.IsSoftDelete)) throw new NotFoundException("Author not found.");
        await ProfileRelationshipTransaction.RunAsync(db, viewerId, author.Id, async () =>
        {
            if (db.Database.IsNpgsql())
            {
                if (blocked)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "UserBlocks" ("BlockerId", "BlockedId", "CreatedAtUtc")
                        VALUES ({viewerId}, {author.Id}, {DateTime.UtcNow})
                        ON CONFLICT ("BlockerId", "BlockedId") DO NOTHING
                        """, ct);
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        DELETE FROM "UserFollows"
                        WHERE ("FollowerId" = {viewerId} AND "FollowedId" = {author.Id})
                           OR ("FollowerId" = {author.Id} AND "FollowedId" = {viewerId})
                        """, ct);
                }
                else await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"UserBlocks\" WHERE \"BlockerId\" = {viewerId} AND \"BlockedId\" = {author.Id}", ct);
            }
            else
            {
                var existing = await db.UserBlocks.FindAsync([viewerId, author.Id], ct);
                if (blocked && existing is null) db.UserBlocks.Add(new UserBlock(viewerId, author.Id));
                if (!blocked && existing is not null) db.UserBlocks.Remove(existing);
                if (blocked) db.UserFollows.RemoveRange(await db.UserFollows.Where(f =>
                    (f.FollowerId == viewerId && f.FollowedId == author.Id) ||
                    (f.FollowerId == author.Id && f.FollowedId == viewerId)).ToArrayAsync(ct));
                await db.SaveChangesAsync(ct);
            }
        }, ct);
        var snapshot = await ReadAuthorsAsync(viewerId, ct);
        return new(publicAuthorId, snapshot.Any(x => x.AuthorId == publicAuthorId), Revision(snapshot));
    }

    public Task<Guid[]> ExcludedUserIdsAsync(Guid viewerId, CancellationToken ct) => db.UserBlocks.AsNoTracking()
        .Where(b => b.BlockerId == viewerId || b.BlockedId == viewerId)
        .Select(b => b.BlockerId == viewerId ? b.BlockedId : b.BlockerId).Distinct().ToArrayAsync(ct);

    public async Task<BlockedAuthorDto?> FindOutgoingAsync(Guid viewerId, Guid targetUserId, CancellationToken ct)
    {
        return (await ReadAuthorsAsync(viewerId, ct, targetUserId)).SingleOrDefault();
    }

    private async Task<BlockedAuthorDto[]> ReadAuthorsAsync(Guid viewerId, CancellationToken ct, Guid? targetUserId = null)
    {
        var blocks = db.UserBlocks.AsNoTracking().Where(b => b.BlockerId == viewerId);
        if (targetUserId.HasValue) blocks = blocks.Where(b => b.BlockedId == targetUserId.Value);
        var rows = await blocks.Join(db.Users.AsNoTracking(), b => b.BlockedId, u => u.Id, (b, u) => new
            {
                u.PublicAuthorId, u.Username, u.PublicSlug, u.SlugRevision, u.IsSoftDelete, u.IsBlocked,
                b.CreatedAtUtc, StorageKey = u.PhotoMetadata != null ? u.PhotoMetadata.StorageKey : null
            }).OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.PublicAuthorId).ToArrayAsync(ct);
        var media = mediaOptions.Value;
        return rows.Select(x => new BlockedAuthorDto(x.PublicAuthorId,
            x.IsSoftDelete ? "Недоступный автор" : x.Username.Value,
            x.IsSoftDelete || x.IsBlocked ? null : MediaStorageUrlBuilder.BuildPublicUrl(media.PublicEndpoint, media.AvatarBucketName, x.StorageKey),
            x.PublicSlug is null || x.IsSoftDelete || x.IsBlocked ? null : new PublicAddressDto(x.PublicSlug, "/users/" + x.PublicSlug, x.SlugRevision),
            x.CreatedAtUtc)).ToArray();
    }

    private static string Revision(BlockedAuthorDto[] items) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join(";", items.Select(x => $"{x.AuthorId:N}:{x.CreatedAtUtc.Ticks}")))));
}
