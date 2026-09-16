using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class ReviewHelpfulVotePersistenceTests
{
    private static ShopsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options;
        return new ShopsDbContext(options);
    }

    [Fact]
    public void ReviewHelpfulVote_HasCompositeKeyOnReviewAndUser()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(ReviewHelpfulVote));

        entity.Should().NotBeNull();
        entity!.FindPrimaryKey()!.Properties.Select(p => p.Name)
            .Should().BeEquivalentTo(nameof(ReviewHelpfulVote.ReviewId), nameof(ReviewHelpfulVote.UserId));
    }

    [Fact]
    public void Review_MapsHelpfulCount()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(Review));

        entity!.FindProperty(nameof(Review.HelpfulCount)).Should().NotBeNull();
    }
}
