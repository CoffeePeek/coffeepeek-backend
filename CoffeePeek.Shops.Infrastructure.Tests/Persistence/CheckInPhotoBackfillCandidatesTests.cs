using CoffeePeek.Contract.Dtos.CheckIns;
using PhotoBackfillPhase = CoffeePeek.Contract.Enums.PhotoBackfillPhase;
using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CheckInPhotoBackfillCandidatesTests
{
    private static readonly DateTime Before = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Recent = Before.AddDays(-7);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static PhotoBackfillPageRequest Request(PhotoBackfillPhase phase) => new(phase, Before, Recent, null);
    private static ShopsDbContext Context() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new CheckInAuditInterceptor()).Options);

    [Fact]
    public async Task PhasesUseCurrentCanonicalEligibilityAndKeepHomeCutoutsWithTheirBindings()
    {
        await using var db = Context();
        var current = Add(db, approved: Before.AddDays(-1));
        var archive = Add(db, approved: Before.AddDays(-10));
        var pending = Add(db, isPublic: true);
        var rejected = Add(db, isPublic: true);
        rejected.Row.ApplyModerationDecision(rejected.Row.ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Rejected, "reason", Before.AddDays(-1));
        var privateRow = Add(db);
        var stale = Add(db, approved: Before.AddDays(-1)); stale.Row.UpdateContent("edited", null);
        var hidden = Add(db, approved: Before.AddDays(-1)); hidden.Shop!.SetHidden(true);
        var home = Add(db, home: true, approved: Before.AddDays(-1));
        var deleted = Add(db, approved: Before.AddDays(-1)); deleted.Row.Delete();
        var newParent = Add(db, approved: Before.AddDays(-1)); newParent.Row.CreatedAtUtc = Before.AddSeconds(1);
        var latePublished = Add(db, approved: Before.AddSeconds(1));
        var latePhoto = Add(db); latePhoto.Row.ShopPhotos.Single().CreatedAtUtc = Before.AddSeconds(1);
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var query = new CheckInPhotoBackfillCandidates(db, TimeProvider.System);
        var recent = await query.ReadAsync(Request(PhotoBackfillPhase.RecentPublicShop), Ct);
        Assert.Equal(current.Row.Id, Assert.Single(recent.Items).CheckInId);
        Assert.Equal(archive.Row.Id, Assert.Single((await query.ReadAsync(Request(PhotoBackfillPhase.PublicShopArchive), Ct)).Items).CheckInId);
        var other = await query.ReadAsync(Request(PhotoBackfillPhase.OtherShop), Ct);
        Assert.Equal(new[] { pending.Row.Id, rejected.Row.Id, privateRow.Row.Id, stale.Row.Id, hidden.Row.Id }.Order(), other.Items.Select(x => x.CheckInId).Order());
        var homePage = await query.ReadAsync(Request(PhotoBackfillPhase.Home), Ct);
        Assert.Equal(2, homePage.Items.Length);
        Assert.Single(homePage.Items, x => x.IsDrink);
        Assert.All(homePage.Items, x =>
        {
            Assert.Equal(home.Row.Id, x.CheckInId); Assert.Equal(home.Row.UserId, x.OwnerId);
            Assert.Equal(CheckInPhotoVariantIdentity.SourceVersion(x.PhotoId, x.StorageKey), x.SourceVersion);
            Assert.Equal(CoffeePeek.Contract.Enums.CheckInKind.Home, x.Kind);
        });
        Assert.DoesNotContain(db.ChangeTracker.Entries(), _ => true);
        Assert.DoesNotContain(recent.Items.Concat(other.Items).Concat(homePage.Items), x => x.CheckInId == deleted.Row.Id || x.CheckInId == newParent.Row.Id || x.CheckInId == latePublished.Row.Id || x.CheckInId == latePhoto.Row.Id);
    }

    [Fact]
    public async Task KeysetUsesBothUuidTieBreakersAndDoesNotDuplicateOrSkipAtPageBoundaries()
    {
        await using var db = Context();
        var rows = Enumerable.Range(0, 3).Select(_ => Add(db, home: true).Row).ToArray();
        foreach (var row in rows)
        {
            row.AddPhotos(Enumerable.Range(0, 3).Select(i => new CheckInPhoto("photo.jpg", "image/jpeg", "private/" + Guid.NewGuid(), 123, row.UserId, i + 1)).ToArray());
            foreach (var photo in row.ShopPhotos) photo.CreatedAtUtc = Before.AddDays(-3);
        }
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var query = new CheckInPhotoBackfillCandidates(db, TimeProvider.System);
        var request = Request(PhotoBackfillPhase.Home) with { Limit = 2 };
        var all = new List<PhotoBackfillCandidate>();
        do
        {
            var page = await query.ReadAsync(request, Ct); Assert.InRange(page.Items.Length, 1, 2);
            all.AddRange(page.Items); request = request with { Cursor = page.NextCursor };
        } while (request.Cursor is not null);
        var expected = rows.SelectMany(r => r.ShopPhotos.Select(p => (CheckInId: r.Id, PhotoId: p.Id)))
            .OrderByDescending(x => x.CheckInId).ThenByDescending(x => x.PhotoId).Select(x => x.PhotoId);
        Assert.Equal(expected, all.Select(x => x.PhotoId)); Assert.Equal(15, all.Select(x => x.PhotoId).Distinct().Count());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task BadOwnerIsOmittedAndReadDoesNotReviseRepublishOrModifyKeys()
    {
        await using var db = Context(); var row = Add(db, approved: Before.AddDays(-1)).Row;
        var bad = Add(db, approved: Before.AddDays(-1)).Row;
        await db.SaveChangesAsync(Ct);
        db.Entry(bad.ShopPhotos.Single()).Property(nameof(CheckInPhoto.OwnerId)).CurrentValue = Guid.NewGuid();
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var revision = row.ContentRevision; var published = row.FirstPublishedAtUtc; var key = row.ShopPhotos.Single().StorageKey;
        var query = new CheckInPhotoBackfillCandidates(db, TimeProvider.System);
        for (var i = 0; i < 2; i++)
            Assert.Equal(row.Id, Assert.Single((await query.ReadAsync(Request(PhotoBackfillPhase.RecentPublicShop), Ct)).Items).CheckInId);
        var unchanged = await db.CheckIns.AsNoTracking().Include(c => c.ShopPhotos).SingleAsync(c => c.Id == row.Id, Ct);
        Assert.Equal(revision, unchanged.ContentRevision); Assert.Equal(published, unchanged.FirstPublishedAtUtc);
        Assert.Equal(key, unchanged.ShopPhotos.Single().StorageKey); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task BadPageCannotCauseAQuery()
    {
        await using var db = Context(); var query = new CheckInPhotoBackfillCandidates(db, TimeProvider.System);
        foreach (var request in new[]
        {
            Request(PhotoBackfillPhase.Home) with { Limit = 0 }, Request(PhotoBackfillPhase.Home) with { Limit = 101 },
            Request((PhotoBackfillPhase)999), Request(PhotoBackfillPhase.Home) with { BeforeUtc = DateTime.UtcNow.AddDays(1) },
            Request(PhotoBackfillPhase.Home) with { RecentSinceUtc = Before.AddSeconds(1) },
            Request(PhotoBackfillPhase.Home) with { Cursor = new PhotoBackfillCursor(Before, Guid.Empty, Guid.NewGuid()) },
            Request(PhotoBackfillPhase.Home) with { BeforeUtc = DateTime.SpecifyKind(Before, DateTimeKind.Unspecified) }
        }) await Assert.ThrowsAsync<ArgumentException>(() => query.ReadAsync(request, Ct));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static (CheckIn Row, CoffeeShop? Shop) Add(ShopsDbContext db, bool home = false, bool isPublic = false, DateTime? approved = null)
    {
        var owner = Guid.NewGuid(); var created = Before.AddDays(-3);
        var shop = home ? null : new CoffeeShop(owner, "Inventory shop", null, PriceRange.Moderate, Guid.NewGuid());
        if (shop is not null) db.Shops.Add(shop);
        var row = home ? CheckIn.CreateHome(owner, "author", null, created, DateOnly.FromDateTime(created), null, null, null, null, null, null)
            : CheckIn.CreateUnified(owner, shop!.Id, "author", null, null, created);
        row.CreatedAtUtc = created;
        row.AddPhotos([new CheckInPhoto("photo.jpg", "image/jpeg", "private/" + Guid.NewGuid(), 123, owner)]);
        if (home) row.AddDrinkPhoto(new CheckInPhoto("cup.png", "image/png", "private/" + Guid.NewGuid(), 456, owner));
        foreach (var photo in row.ShopPhotos) photo.CreatedAtUtc = created;
        if (isPublic || approved.HasValue) row.ChangeVisibility(CheckInVisibility.Public);
        if (approved is { } at) row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, at);
        db.CheckIns.Add(row); return (row, shop);
    }
}
