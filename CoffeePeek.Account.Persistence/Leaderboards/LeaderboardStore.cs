using CoffeePeek.Account.Application.Features.Leaderboards;
using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Contract.Dtos.Leaderboards;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Events.Leaderboards;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Leaderboards;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Persistence.Leaderboards;

public sealed class LeaderboardStore(AccountDbContext db, IOptions<MediaPublicUrlOptions> mediaOptions) : ILeaderboardStore
{
    public async Task ApplyAsync(ActivityScoreChanged message, CancellationToken ct)
    {
        if (!Enum.IsDefined(message.Kind) || message.SourceId == Guid.Empty || message.UserId == Guid.Empty
            || message.Version < 1 || message.IsEligible && message.CreditedAtUtc == null
            || message.CreditedAtUtc is { } date && (date == default || date.Kind != DateTimeKind.Utc))
            throw new ValidationException("Invalid leaderboard activity.");
        var points = !message.IsEligible ? 0 : message.Kind switch
        {
            LeaderboardActivityKind.ShopAdded or LeaderboardActivityKind.RoasterAdded => 3,
            LeaderboardActivityKind.CheckIn => 2,
            _ => 1
        };
        if (db.Database.IsNpgsql())
        {
            // Keep tombstones for revocations arriving before grants, and never move a restored credit to a new period.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ActivityScores" ("Kind", "SourceId", "UserId", "Version", "Points", "CreditedAtUtc")
                SELECT {(int)message.Kind}, {message.SourceId}, "Id", {message.Version}, {points}, {message.CreditedAtUtc}
                FROM "Users" WHERE "Id" = {message.UserId} AND NOT "IsSoftDelete"
                ON CONFLICT ("Kind", "SourceId") DO UPDATE
                SET "Version" = EXCLUDED."Version", "Points" = EXCLUDED."Points",
                    "CreditedAtUtc" = COALESCE("ActivityScores"."CreditedAtUtc", EXCLUDED."CreditedAtUtc")
                WHERE "ActivityScores"."Version" < EXCLUDED."Version"
                    AND "ActivityScores"."UserId" = EXCLUDED."UserId"
                """, ct);
            return;
        }

        if (!await db.Users.AnyAsync(x => x.Id == message.UserId && !x.IsSoftDelete, ct)) return;
        var row = await db.ActivityScores.FindAsync([message.Kind, message.SourceId], ct);
        if (row is not null && (row.Version >= message.Version || row.UserId != message.UserId)) return;
        if (row is null)
        {
            row = new ActivityScore { Kind = message.Kind, SourceId = message.SourceId, UserId = message.UserId };
            db.ActivityScores.Add(row);
        }
        row.Version = message.Version;
        row.Points = points;
        row.CreditedAtUtc ??= message.CreditedAtUtc;
        await db.SaveChangesAsync(ct);
    }

    public async Task<LeaderboardPageDto> GetAsync(LeaderboardPeriod period, int page, int pageSize,
        Guid? viewerId, CancellationToken ct)
    {
        if (page is < 1 or > 10000 || pageSize is < 1 or > 100)
            throw new ValidationException("Page must be 1–10000 and pageSize must be 1–100.");
        await using var snapshot = db.Database.IsNpgsql() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct) : null;
        var users = db.Users.AsNoTracking().Where(u => !u.IsBlocked && !u.IsSoftDelete
            && !u.Roles.Any(r => r.Name == RoleConsts.Admin || r.Name == RoleConsts.Moderator));
        if (viewerId.HasValue)
            users = users.Where(u => !db.UserBlocks.Any(b =>
                b.BlockerId == viewerId && b.BlockedId == u.Id || b.BlockedId == viewerId && b.BlockerId == u.Id));
        var totals = db.ActivityScores.AsNoTracking()
            .Where(x => x.Points > 0 && x.CreditedAtUtc >= period.StartsAtUtc && x.CreditedAtUtc < period.EndsAtUtc
                && users.Any(u => u.Id == x.UserId))
            .GroupBy(x => x.UserId).Select(g => new LeaderboardTotal
            {
                UserId = g.Key, Points = g.Sum(x => x.Points),
                Additions = g.Count(x => x.Kind == LeaderboardActivityKind.ShopAdded || x.Kind == LeaderboardActivityKind.RoasterAdded),
                Edits = g.Count(x => x.Kind == LeaderboardActivityKind.ShopEdited || x.Kind == LeaderboardActivityKind.RoasterEdited),
                CheckIns = g.Count(x => x.Kind == LeaderboardActivityKind.CheckIn)
            });
        var count = await totals.CountAsync(ct);
        var offset = (page - 1) * pageSize;
        var rows = await totals.OrderByDescending(x => x.Points).ThenBy(x => x.UserId)
            .Skip(offset).Take(pageSize).ToArrayAsync(ct);
        var own = viewerId.HasValue ? await totals.SingleOrDefaultAsync(x => x.UserId == viewerId, ct) : null;
        var ids = rows.Select(x => x.UserId).Concat(own is null ? [] : new[] { own.UserId }).Distinct().ToArray();
        var profiles = await users.Where(u => ids.Contains(u.Id)).Select(u => new
        {
            u.Id, u.PublicAuthorId, u.Username, u.PublicSlug, u.SlugRevision,
            StorageKey = u.PhotoMetadata == null ? null : u.PhotoMetadata.StorageKey
        }).ToDictionaryAsync(u => u.Id, ct);
        LeaderboardEntryDto Entry(LeaderboardTotal row, int rank)
        {
            var profile = profiles[row.UserId];
            var media = mediaOptions.Value;
            return new(rank, profile.PublicAuthorId, profile.Username.Value,
                MediaStorageUrlBuilder.BuildPublicUrl(media.PublicEndpoint, media.AvatarBucketName, profile.StorageKey),
                profile.PublicSlug is null ? null : new PublicAddressDto(profile.PublicSlug, "/users/" + profile.PublicSlug, profile.SlugRevision),
                row.Points, row.Additions, row.Edits, row.CheckIns);
        }
        var items = new List<LeaderboardEntryDto>();
        var rank = rows.Length == 0 ? 0 : 1 + await totals.CountAsync(x => x.Points > rows[0].Points, ct);
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0 && rows[i].Points != rows[i - 1].Points) rank = offset + i + 1;
            items.Add(Entry(rows[i], rank));
        }
        var me = own is null ? null : Entry(own, 1 + await totals.CountAsync(x => x.Points > own.Points, ct));
        return new(period.Kind, period.StartsOn, period.EndsOnExclusive, LeaderboardPeriod.TimeZoneId,
            items, count, page, pageSize, me);
    }
}
