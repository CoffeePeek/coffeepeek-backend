using CoffeePeek.Contract.Dtos.CheckIns;
using PhotoBackfillPhase = CoffeePeek.Contract.Enums.PhotoBackfillPhase;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CheckInPhotoBackfillCandidatesLiveTests(ShopsLiveDatabase fixture) : IClassFixture<ShopsLiveDatabase>
{
    [Fact]
    public async Task PostgreSqlPaginatesUuidTiesAndAllPhasesInOneBoundedSelectWithoutTrackingOrMutation()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        // The generic live fixture uses AuditInterceptor, which resets historical Added stamps.
        // Shops production preserves canonical creation timestamps through its specialised interceptor.
        await using var writer = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.ConfigureDataSource(ds => ds.EnableDynamicJson()))
            .AddInterceptors(new CheckInAuditInterceptor()).Options);
        var before = DateTime.UtcNow.AddMinutes(-1); var created = before.AddDays(-2);
        var city = new City("photo inventory city"); writer.Cities.Add(city);
        var shop = ShopsLiveDatabase.NewShop("Photo inventory", city.Id); writer.Shops.Add(shop);
        var rows = new[]
        {
            CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "recent", null, null, created),
            CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "archive", null, null, created),
            CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "private", null, null, created),
            CheckIn.CreateHome(Guid.NewGuid(), "home", null, created, DateOnly.FromDateTime(created), null, null, null, null, null, null)
        };
        foreach (var row in rows)
        {
            row.CreatedAtUtc = created;
            row.AddPhotos(Enumerable.Range(0, 3).Select(i => new CheckInPhoto("source.jpg", "image/jpeg", "private/" + Guid.NewGuid(), 12345, row.UserId, i)).ToArray());
            foreach (var photo in row.ShopPhotos) photo.CreatedAtUtc = created;
            writer.CheckIns.Add(row);
        }
        rows[0].ChangeVisibility(CheckInVisibility.Public);
        rows[0].ApplyModerationDecision(rows[0].ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, before.AddHours(-1));
        rows[1].ChangeVisibility(CheckInVisibility.Public);
        rows[1].ApplyModerationDecision(rows[1].ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, before.AddDays(-1));
        await writer.SaveChangesAsync(ct);
        writer.ChangeTracker.Clear();
        var parentIds = rows.Select(r => r.Id).ToArray();
        var baseline = await writer.CheckIns.AsNoTracking().Where(c => parentIds.Contains(c.Id))
            .Select(c => new { c.Id, c.CreatedAtUtc, c.ContentRevision, c.FirstPublishedAtUtc }).ToDictionaryAsync(c => c.Id, ct);
        var photoBaseline = await writer.CheckInPhotos.AsNoTracking().Where(p => parentIds.Contains(p.CanonicalCheckInId))
            .Select(p => new { p.Id, p.StorageKey, p.CreatedAtUtc }).ToArrayAsync(ct);
        Assert.Equal(4, baseline.Count); Assert.Equal(12, photoBaseline.Length);
        Assert.All(baseline.Values, c => Assert.True(c.CreatedAtUtc <= before, "Seeded parent must predate the fixed inventory cutoff."));
        Assert.All(photoBaseline, p => Assert.True(p.CreatedAtUtc <= before, "Seeded photo must predate the fixed inventory cutoff."));
        var expected = photoBaseline.Select(p => p.Id).Order().ToArray();
        using var probe = new ReadPerformanceCommandProbe(capturePlans: true);
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options);
        var query = new CheckInPhotoBackfillCandidates(db, TimeProvider.System);
        var actual = new List<Guid>();
        foreach (var phase in Enum.GetValues<PhotoBackfillPhase>())
        {
            var request = new PhotoBackfillPageRequest(phase, before, before.AddHours(-2), null, 2);
            var rowId = phase switch
            {
                PhotoBackfillPhase.RecentPublicShop => rows[0].Id,
                PhotoBackfillPhase.PublicShopArchive => rows[1].Id,
                PhotoBackfillPhase.OtherShop => rows[2].Id,
                _ => rows[3].Id
            };
            var phasePhotos = new List<Guid>();
            do
            {
                var previousCalls = probe.Count; var page = await query.ReadAsync(request, ct);
                Assert.Equal(previousCalls + 1, probe.Count); Assert.InRange(page.Items.Length, 1, 2);
                Assert.All(page.Items, x => Assert.Equal(rowId, x.CheckInId));
                Assert.DoesNotContain("OFFSET", probe.Commands[^1].CommandText, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("LIMIT", probe.Commands[^1].CommandText, StringComparison.OrdinalIgnoreCase);
                phasePhotos.AddRange(page.Items.Select(x => x.PhotoId)); request = request with { Cursor = page.NextCursor };
            } while (request.Cursor is not null);
            Assert.Equal(phasePhotos.OrderDescending(), phasePhotos); actual.AddRange(phasePhotos);
        }
        Assert.Equal(expected, actual.Order()); Assert.Empty(db.ChangeTracker.Entries());
        var after = await writer.CheckIns.AsNoTracking().Where(c => parentIds.Contains(c.Id)).ToArrayAsync(ct);
        foreach (var row in after)
        {
            var persisted = baseline[row.Id];
            Assert.Equal(persisted.ContentRevision, row.ContentRevision); Assert.Equal(persisted.FirstPublishedAtUtc, row.FirstPublishedAtUtc);
        }
        var photoAfter = await writer.CheckInPhotos.AsNoTracking().Where(p => parentIds.Contains(p.CanonicalCheckInId))
            .Select(p => new { p.Id, p.StorageKey, p.CreatedAtUtc }).ToArrayAsync(ct);
        Assert.Equal(photoBaseline.OrderBy(p => p.Id), photoAfter.OrderBy(p => p.Id));
    }
}
