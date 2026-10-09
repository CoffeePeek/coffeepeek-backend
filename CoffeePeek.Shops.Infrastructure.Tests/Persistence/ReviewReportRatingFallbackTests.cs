using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Features.Review.Reports;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class ReviewReportRatingFallbackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingReportedLatestVisitRestoresPriorScoreAndRetainsReportAudit(bool migrated)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var shop = new CoffeeShop(null, "shop", null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetLocation(Guid.NewGuid(), "address", 53m, 27m);
        var user = Guid.NewGuid();
        var time = DateTime.UtcNow.AddYears(-2);
        var earlier = Approved(user, shop.Id, 2, time);
        var targetId = migrated ? Guid.Parse("d59022f5-cb38-4c18-a798-b44164e17a68") : Guid.NewGuid();
        var latest = Approved(user, shop.Id, 5, time.AddDays(1), targetId);
        var other = Approved(Guid.NewGuid(), shop.Id, 4, time);
        var report = ReviewReport.Create(targetId, Guid.NewGuid(), "historical complaint");
        report.CreatedAtUtc = time.AddDays(2);
        var originalAudit = ReviewReportDto.From(report);
        db.Shops.Add(shop);
        db.CheckIns.AddRange(earlier, latest, other);
        db.ReviewReports.Add(report);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var ratings = new CheckInQueries(db);
        Assert.Equal((4.5m, 3), await ratings.GetStatsByShopAsync(shop.Id, ct));
        var reports = new ReviewReportRepository(db);
        var checkIns = new QueryCheckInRepository(db);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(ct)).Returns(() => db.SaveChangesAsync(ct));
        var bus = new Mock<IMessageBus>();
        var admin = Guid.NewGuid();
        var command = new ResolveReviewReportCommand(report.Id, admin, true);

        await ResolveReviewReportHandler.Handle(command, reports, checkIns, uow.Object, bus.Object, ct);

        Assert.Equal((3m, 2), await ratings.GetStatsByShopAsync(shop.Id, ct));
        var deletedAt = (await db.CheckIns.SingleAsync(c => c.Id == targetId, ct)).DeletedAtUtc;
        Assert.NotNull(deletedAt);
        var resolved = await GetReviewReportHandler.Handle(new(report.Id), reports, checkIns, ct);
        Assert.Equal(originalAudit.Id, resolved.Data.Report.Id);
        Assert.Equal(originalAudit.CheckInId, resolved.Data.Report.CheckInId);
        Assert.Equal(originalAudit.Text, resolved.Data.Report.Text);
        Assert.Equal(originalAudit.ReportedByUserId, resolved.Data.Report.ReportedByUserId);
        Assert.Equal(originalAudit.CreatedAtUtc, resolved.Data.Report.CreatedAtUtc);
        Assert.Equal(admin, resolved.Data.Report.ResolvedByAdminId);
        Assert.Equal(ReviewReportStatus.CheckInDeleted, resolved.Data.Report.Status);
        Assert.Equal(targetId, resolved.Data.CheckIn!.Id);
        Assert.Equal(deletedAt, resolved.Data.CheckIn.DeletedAtUtc);
        Assert.False(resolved.Data.CheckIn.IsPubliclyVisible);
        db.ChangeTracker.Clear();

        await ResolveReviewReportHandler.Handle(command with { AdminId = Guid.NewGuid() },
            reports, checkIns, uow.Object, bus.Object, ct);

        Assert.Equal((3m, 2), await ratings.GetStatsByShopAsync(shop.Id, ct));
        Assert.Equal(deletedAt, (await db.CheckIns.SingleAsync(c => c.Id == targetId, ct)).DeletedAtUtc);
        Assert.Equal(resolved.Data.Report, ReviewReportDto.From(await db.ReviewReports.SingleAsync(ct)));
        Assert.True((await db.CheckIns.SingleAsync(c => c.Id == earlier.Id, ct)).IsPubliclyVisible);
        Assert.True((await db.CheckIns.SingleAsync(c => c.Id == other.Id, ct)).IsPubliclyVisible);
        uow.Verify(x => x.SaveChangesAsync(ct), Times.Once);
        bus.Verify(x => x.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(message =>
            message.CheckInId == targetId && message.ShopId == shop.Id)), Times.Once);
    }

    private static CheckIn Approved(Guid user, Guid shop, int score, DateTime visited, Guid? id = null)
    {
        var visit = CheckIn.CreateUnified(user, shop, "author", "original review",
            Rating.Create(score, score, score), visited, CheckInVisibility.Public, id: id);
        visit.CreatedAtUtc = visited.AddHours(1);
        visit.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, visit.CreatedAtUtc);
        return visit;
    }
}
