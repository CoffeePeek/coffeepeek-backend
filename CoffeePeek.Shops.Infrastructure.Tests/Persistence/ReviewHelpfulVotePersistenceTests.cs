using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class CheckInHelpfulVotePersistenceTests
{
    private static ShopsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options;
        return new ShopsDbContext(options);
    }

    [Fact]
    public void CheckInHelpfulVote_HasCompositeKeyOnReviewAndUser()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(CheckInHelpfulVote));

        entity.Should().NotBeNull();
        entity!.FindPrimaryKey()!.Properties.Select(p => p.Name)
            .Should().BeEquivalentTo(nameof(CheckInHelpfulVote.CheckInId), nameof(CheckInHelpfulVote.UserId));
    }

    [Fact]
    public void Review_MapsHelpfulCount()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(CheckIn));

        entity!.FindProperty(nameof(CheckIn.HelpfulCount)).Should().NotBeNull();
    }
}
