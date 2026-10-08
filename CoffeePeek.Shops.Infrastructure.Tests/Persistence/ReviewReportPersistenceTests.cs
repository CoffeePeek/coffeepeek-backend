using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class ReviewReportPersistenceTests
{
    [Fact]
    public void Model_ConstrainsTextReferencesAndUsesXminConcurrency()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=test;Password=test").Options);
        var report = db.Model.FindEntityType(typeof(ReviewReport))!;
        report.FindProperty(nameof(ReviewReport.Text))!.GetMaxLength().Should().Be(2000);
        report.FindProperty(nameof(ReviewReport.Text))!.IsNullable.Should().BeFalse();
        report.GetForeignKeys().Should().BeEmpty();
        var target = report.FindProperty(nameof(ReviewReport.CheckInId))!;
        target.GetColumnName().Should().Be("ReviewId");
        target.IsNullable.Should().BeFalse();
        foreach (var entity in new[] { report })
        {
            var version = entity.FindProperty("Version")!;
            version.IsConcurrencyToken.Should().BeTrue();
            version.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);
            version.GetColumnName().Should().Be("xmin");
        }
    }

    [Fact]
    public async Task Repository_ReturnsStablePendingPagesAndPreservesResolvedReports()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "tester", "comment text body",
            Rating.Create(5, 5, 5), DateTime.UtcNow);
        var reports = Enumerable.Range(0, 3).Select(_ => ReviewReport.Create(checkIn.Id, Guid.NewGuid(), "abuse")).ToArray();
        foreach (var report in reports) report.CreatedAtUtc = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
        reports[2].Resolve(false, Guid.NewGuid());
        db.CheckIns.Add(checkIn);
        db.ReviewReports.AddRange(reports);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var repository = new ReviewReportRepository(db);
        var (first, total) = await repository.GetPageAsync(ReviewReportStatus.Pending, 1, 1, TestContext.Current.CancellationToken);
        var (second, _) = await repository.GetPageAsync(ReviewReportStatus.Pending, 2, 1, TestContext.Current.CancellationToken);
        total.Should().Be(2);
        new[] { first.Single().Id, second.Single().Id }.Should().Equal(reports.Take(2).OrderBy(x => x.Id).Select(x => x.Id));
        var (_, allCount) = await repository.GetPageAsync(null, 1, 20, TestContext.Current.CancellationToken);
        allCount.Should().Be(3);
        first.Single().CheckInId.Should().Be(checkIn.Id);
        db.CheckIns.Remove(checkIn);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        (await repository.GetByIdAsync(reports[0].Id, TestContext.Current.CancellationToken))!.CheckInId.Should().Be(checkIn.Id);
        (await repository.GetByIdAsync(reports[2].Id, TestContext.Current.CancellationToken))!.Status.Should().Be(ReviewReportStatus.Dismissed);
    }
}
