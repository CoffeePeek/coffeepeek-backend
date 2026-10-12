using CoffeePeek.Moderation.Application.Common.Models;
using CoffeePeek.Moderation.Application.Features.Admin.Stats;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeeShop.Moderation.Persistence.Configuration;
using CoffeeShop.Moderation.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ModerationReview = CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate.ModerationReview;

namespace CoffeePeek.ModerationService.Tests;

public class AdminModerationStatsQueryRepositoryTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static ModerationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ModerationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ModerationReview Review(DateTime createdAtUtc)
    {
        var review = ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1,
            Guid.NewGuid(), Guid.NewGuid(), "user", "A complete check-in comment",
            4, 4, 4, null, createdAtUtc, []);
        review.CreatedAtUtc = createdAtUtc;
        return review;
    }

    private static ModerationAuditLog Audit(Guid moderatorId, ModerationAuditAction action) =>
        ModerationAuditLog.Create(ModerationAuditEntityType.Review, Guid.NewGuid(), "entity", action, moderatorId, null);

    [Fact]
    public async Task GetInsightsAsync_Sla_AveragesModeratedCheckInsInLast30Days()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;
        var fast = Review(now.AddHours(-2));
        fast.Approve(Guid.NewGuid());
        var slow = Review(now.AddHours(-4));
        slow.Reject("Not a real review at all", Guid.NewGuid());
        var pending = Review(now.AddHours(-50));
        var old = Review(now.AddDays(-60));
        old.Approve(Guid.NewGuid());
        db.ModerationReviews.AddRange(fast, slow, pending, old);
        db.Entry(old).Property(x => x.ModeratedAt).CurrentValue = now.AddDays(-40);
        await db.SaveChangesAsync(_ct);

        var insights = await new AdminModerationStatsQueryRepository(db).GetInsightsAsync(DateTime.UtcNow, _ct);

        insights.CheckInsModerated.Should().Be(2);
        insights.AvgCheckInModerationHours.Should().BeApproximately(3.0, 0.01);

        var response = await GetAdminModerationInsightsHandler.Handle(
            new GetAdminModerationInsightsQuery(), new AdminModerationStatsQueryRepository(db), _ct);
        response.Data.Sla.CheckInsModerated30Days.Should().Be(2);
        response.Data.Sla.AvgCheckInModerationHours.Should().Be(3.0);
    }

    [Fact]
    public async Task GetInsightsAsync_NothingModerated_ReturnsNullAverageAndEmptyQueues()
    {
        await using var db = CreateContext();

        var response = await GetAdminModerationInsightsHandler.Handle(
            new GetAdminModerationInsightsQuery(), new AdminModerationStatsQueryRepository(db), _ct);

        response.Data.Sla.CheckInsModerated30Days.Should().Be(0);
        response.Data.Sla.AvgCheckInModerationHours.Should().BeNull();
        response.Data.OldestPendingHours.Should().BeNull();
        response.Data.Queues.Select(q => q.Queue).Should()
            .Equal("shops", "checkIns", "roasters", "changeRequests", "issueReports");
        response.Data.Queues.Should().OnlyContain(q => q.Pending == 0 && q.OldestPendingHours == null);
        response.Data.TopModerators30Days.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Queues_ReportPendingCountsAndOldestAge()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;

        var pendingShop = ModerationShop.Create("Pending shop", Guid.NewGuid(), Guid.NewGuid(), null);
        pendingShop.CreatedAtUtc = now.AddHours(-10);
        db.ModerationShops.Add(pendingShop);

        var pendingReview = Review(now.AddHours(-30));
        var approvedReview = Review(now.AddHours(-100));
        approvedReview.Approve(Guid.NewGuid());
        db.ModerationReviews.AddRange(pendingReview, approvedReview);

        var changeRequest = ShopChangeRequest.Create(Guid.NewGuid(), Guid.NewGuid(), ShopChangeSection.Contacts, "{}");
        changeRequest.CreatedAtUtc = now.AddHours(-5);
        db.ShopChangeRequests.Add(changeRequest);

        var issueReport = ShopIssueReport.Create(Guid.NewGuid(), Guid.NewGuid(), ShopIssueCategory.ShopClosed, null);
        issueReport.CreatedAtUtc = now.AddHours(-2);
        db.ShopIssueReports.Add(issueReport);
        await db.SaveChangesAsync(_ct);

        var response = await GetAdminModerationInsightsHandler.Handle(
            new GetAdminModerationInsightsQuery(), new AdminModerationStatsQueryRepository(db), _ct);

        var queues = response.Data.Queues.ToDictionary(q => q.Queue);
        queues["shops"].Pending.Should().Be(1);
        queues["shops"].OldestPendingHours.Should().Be(10.0);
        queues["checkIns"].Pending.Should().Be(1);
        queues["checkIns"].OldestPendingHours.Should().Be(30.0);
        queues["roasters"].Pending.Should().Be(0);
        queues["roasters"].OldestPendingHours.Should().BeNull();
        queues["changeRequests"].Pending.Should().Be(1);
        queues["changeRequests"].OldestPendingHours.Should().Be(5.0);
        queues["issueReports"].Pending.Should().Be(1);
        queues["issueReports"].OldestPendingHours.Should().Be(2.0);
        response.Data.OldestPendingHours.Should().Be(30.0);
    }

    [Fact]
    public async Task GetInsightsAsync_Workload_GroupsByModeratorLast30Days()
    {
        await using var db = CreateContext();
        var mod1 = Guid.NewGuid();
        var mod2 = Guid.NewGuid();
        var old = Audit(mod2, ModerationAuditAction.Approved);
        db.ModerationAuditLogs.AddRange(
            Audit(mod1, ModerationAuditAction.Approved),
            Audit(mod1, ModerationAuditAction.Approved),
            Audit(mod1, ModerationAuditAction.Rejected),
            Audit(mod2, ModerationAuditAction.Approved),
            old);
        db.Entry(old).Property(x => x.CreatedAtUtc).CurrentValue = DateTime.UtcNow.AddDays(-45);
        await db.SaveChangesAsync(_ct);

        var insights = await new AdminModerationStatsQueryRepository(db).GetInsightsAsync(DateTime.UtcNow, _ct);

        insights.Workload.Should().Equal(
            new AdminModeratorWorkload(mod1, 3, 2, 1),
            new AdminModeratorWorkload(mod2, 1, 1, 0));
    }

    [Fact]
    public async Task GetInsightsAsync_Workload_LimitedToTen()
    {
        await using var db = CreateContext();
        for (var i = 0; i < 12; i++)
            db.ModerationAuditLogs.Add(Audit(Guid.NewGuid(), ModerationAuditAction.Approved));
        await db.SaveChangesAsync(_ct);

        var insights = await new AdminModerationStatsQueryRepository(db).GetInsightsAsync(DateTime.UtcNow, _ct);

        insights.Workload.Should().HaveCount(10);
    }
}
