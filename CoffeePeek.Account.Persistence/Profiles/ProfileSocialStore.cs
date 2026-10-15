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

public sealed class ProfileSocialStore(AccountDbContext db, IOptions<MediaPublicUrlOptions> mediaOptions) : IProfileSocialStore
{
    private IQueryable<User> VisibleUsers => db.Users.AsNoTracking()
        .Where(u => !u.IsBlocked && !u.IsSoftDelete && u.PublicSlug != null);

    public async Task<ProfileFollowStateDto> GetStateAsync(Guid userId, Guid? viewerId, CancellationToken ct)
    {
        await RequireVisibleAsync(userId, ct);
        var followers = db.UserFollows.AsNoTracking().Where(f => f.FollowedId == userId &&
            VisibleUsers.Any(u => u.Id == f.FollowerId));
        var count = await followers.CountAsync(ct);
        var isFollowing = viewerId.HasValue && await followers.AnyAsync(f => f.FollowerId == viewerId.Value, ct);
        return new(count, isFollowing);
    }

    public async Task<ProfileFollowPageDto> ListAsync(Guid userId, bool following, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 10000 || pageSize is < 1 or > 100)
            throw new ValidationException("Page must be 1–10000 and pageSize must be 1–100.");
        await RequireVisibleAsync(userId, ct);
        var links = db.UserFollows.AsNoTracking()
            .Where(f => following ? f.FollowerId == userId : f.FollowedId == userId);
        var query = links.Join(VisibleUsers,
            f => following ? f.FollowedId : f.FollowerId,
            u => u.Id,
            (f, u) => new
            {
                u.Id,
                u.Username,
                u.PublicSlug,
                u.SlugRevision,
                f.CreatedAtUtc,
                StorageKey = u.PhotoMetadata != null ? u.PhotoMetadata.StorageKey : null
            });
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var media = mediaOptions.Value;
        return new(rows.Select(row => new ProfileFollowUserDto(row.Username.Value,
            MediaStorageUrlBuilder.BuildPublicUrl(media.PublicEndpoint, media.AvatarBucketName, row.StorageKey),
            new PublicAddressDto(row.PublicSlug!, "/users/" + row.PublicSlug, row.SlugRevision))).ToArray(),
            count, page, pageSize);
    }

    public async Task SetAsync(Guid followerId, Guid followedId, bool following, CancellationToken ct)
    {
        if (followerId == followedId) throw new ValidationException("You cannot follow yourself.");
        await RequireVisibleAsync(followerId, ct);
        await RequireVisibleAsync(followedId, ct);
        if (db.Database.IsNpgsql())
        {
            if (following)
            {
                // A single INSERT and its composite key make concurrent PUTs idempotent.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "UserFollows" ("FollowerId", "FollowedId", "CreatedAtUtc")
                    SELECT {followerId}, {followedId}, {DateTime.UtcNow}
                    WHERE EXISTS (SELECT 1 FROM "Users" WHERE "Id" = {followerId} AND NOT "IsBlocked" AND NOT "IsSoftDelete")
                      AND EXISTS (SELECT 1 FROM "Users" WHERE "Id" = {followedId} AND NOT "IsBlocked" AND NOT "IsSoftDelete")
                    ON CONFLICT ("FollowerId", "FollowedId") DO NOTHING
                    """, ct);
            }
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"UserFollows\" WHERE \"FollowerId\" = {followerId} AND \"FollowedId\" = {followedId}", ct);
            return;
        }

        // EF's nonrelational provider is also used by focused repository tests.
        var row = await db.UserFollows.FindAsync([followerId, followedId], ct);
        if (following && row is null) db.UserFollows.Add(new UserFollow(followerId, followedId));
        if (!following && row is not null) db.UserFollows.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    private async Task RequireVisibleAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && !u.IsBlocked && !u.IsSoftDelete, ct))
            throw new NotFoundException("User not found");
    }
}
