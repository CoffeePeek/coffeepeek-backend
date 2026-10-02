using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
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
        report.GetForeignKeys().Single().PrincipalEntityType.ClrType.Should().Be(typeof(Review));
        foreach (var entity in new[] { report, db.Model.FindEntityType(typeof(Review))! })
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
        var review = Review.Create(Guid.NewGuid(), Guid.NewGuid(), "tester", null, "comment text body", 5, 5, 5, Guid.NewGuid());
        var reports = Enumerable.Range(0, 3).Select(_ => ReviewReport.Create(review.Id, Guid.NewGuid(), "abuse")).ToArray();
        foreach (var report in reports) report.CreatedAtUtc = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
        reports[2].Resolve(false, Guid.NewGuid());
        db.Reviews.Add(review);
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
        (await repository.GetByIdAsync(reports[2].Id, TestContext.Current.CancellationToken))!.Status.Should().Be(ReviewReportStatus.Dismissed);
    }
}
