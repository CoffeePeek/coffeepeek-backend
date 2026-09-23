using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using CoffeeShop.Moderation.Persistence.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.ModerationService.Tests;

public class ConcurrencyTokenModelTests
{
    public static TheoryData<Type> DecidedAggregates =>
    [
        typeof(ModerationShop),
        typeof(ModerationRoaster),
        typeof(ModerationReview),
        typeof(ShopChangeRequest),
        typeof(ShopImportCandidate),
        typeof(ShopIssueReport)
    ];

    [Theory]
    [MemberData(nameof(DecidedAggregates))]
    public void DecidedAggregate_HasXminConcurrencyToken(Type entityType)
    {
        // Regression: without a token two concurrent approvals both published the entity.
        using var db = new ModerationDbContext(
            new DbContextOptionsBuilder<ModerationDbContext>().UseNpgsql("Host=unused").Options);

        var token = db.Model.FindEntityType(entityType)!.GetProperties().SingleOrDefault(p => p.IsConcurrencyToken);

        token.Should().NotBeNull();
        token!.GetColumnName().Should().Be("xmin");
    }
}
