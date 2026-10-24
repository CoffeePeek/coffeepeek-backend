using CoffeePeek.Testing;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.ModerationService.Tests;

public sealed class GeocodingLiveDatabase : LiveDatabase<ModerationDbContext>
{
    protected override string Service => "moderation";
    protected override ModerationDbContext Create(DbContextOptions<ModerationDbContext> options, bool enabled) => new(options);
}
