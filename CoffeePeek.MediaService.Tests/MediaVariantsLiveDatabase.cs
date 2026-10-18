using CoffeePeek.MediaService.Data;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.MediaService.Tests;

public sealed class MediaVariantsLiveDatabase : LiveDatabase<MediaDbContext>
{
    protected override string Service => "mediavariants";
    protected override MediaDbContext Create(DbContextOptions<MediaDbContext> options, bool enabled) => new(options);
}
