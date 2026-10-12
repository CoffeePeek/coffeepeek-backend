using CoffeePeek.MediaService.Data;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public sealed class MediaLiveDatabase : LiveDatabase<MediaDbContext>
{
    protected override string Service => "mediafault";
    protected override MediaDbContext Create(DbContextOptions<MediaDbContext> options, bool enabled) => new(options);
}
