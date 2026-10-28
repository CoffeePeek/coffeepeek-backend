using System.Text.Json;
using CoffeePeek.Moderation.Application.Features.Import;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StackExchange.Redis;

var apply = args.LastOrDefault() == "--apply";
var sourceArgs = apply ? args[..^1] : args;
if (sourceArgs.Length > 1 || sourceArgs.Any(a => a.StartsWith("--")))
    throw new ArgumentException("Use [reviewed-sources.json] [--apply]; omit --apply for a dry run.");
var reviewed = sourceArgs.Length == 0 ? new Dictionary<string, string>()
    : JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(sourceArgs[0]))
      ?? throw new ArgumentException("Reviewed sources must map shop slugs to local opening hours.");
foreach (var hours in reviewed.Values)
    if (ImportOpeningHoursParser.Parse(hours) is null) throw new ArgumentException("Reviewed hours cannot be empty.");
var connection = Environment.GetEnvironmentVariable("PostgresCpOptions__ConnectionString")
    ?? throw new InvalidOperationException("PostgresCpOptions__ConnectionString is required.");
var sourceConnection = new NpgsqlConnectionStringBuilder(connection)
{
    Database = AppResources.ModerationDb,
    Options = "-c default_transaction_read_only=on -c statement_timeout=10000"
};
await using var shops = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
    .UseNpgsql(connection).AddInterceptors(new AuditInterceptor()).Options);
await using var moderation = new ModerationDbContext(new DbContextOptionsBuilder<ModerationDbContext>()
    .UseNpgsql(sourceConnection.ConnectionString).Options);
var missing = await shops.Shops.Where(s => !s.Schedules.Any())
    .Include(s => s.Schedules).ThenInclude(d => d.Intervals).ToListAsync();
var shopIds = missing.Select(s => s.Id).ToArray();
var moderationIds = missing.Where(s => s.ModerationId.HasValue).Select(s => s.ModerationId!.Value).ToArray();
var sources = await moderation.ShopImportCandidates.AsNoTracking()
    .Where(c => moderationIds.Contains(c.Id) || c.ResultingShopId.HasValue && shopIds.Contains(c.ResultingShopId.Value))
    .Select(c => new { c.Id, c.ResultingShopId, c.OpeningHours }).ToListAsync();
var planned = new List<Guid>();
var unavailable = new List<string>();
foreach (var shop in missing)
{
    var source = sources.FirstOrDefault(c => c.Id == shop.ModerationId || c.ResultingShopId == shop.Id);
    var hours = string.IsNullOrWhiteSpace(source?.OpeningHours)
        ? reviewed.GetValueOrDefault(shop.PublicSlug ?? "") : source.OpeningHours;
    var parsed = ImportOpeningHoursParser.Parse(hours);
    if (parsed is null)
    {
        unavailable.Add(shop.Name);
        continue;
    }
    var replacement = parsed.Select(d => ShopSchedule.Create(d.DayOfWeek, d.IsClosed,
        (d.Intervals ?? []).Select(i => ShopScheduleInterval.Create(i.OpenTime, i.CloseTime)).ToList())).ToArray();
    var result = shop.ReplaceSchedules(replacement);
    if (result.IsFailed) throw new InvalidOperationException(result.Errors[0].Message);
    shop.AdvanceProfileRevision();
    planned.Add(shop.Id);
}
Console.WriteLine(JsonSerializer.Serialize(new { apply, missing = missing.Count, restorable = planned.Count,
    unavailable = unavailable.Count, unavailableNames = unavailable }));
if (!apply || planned.Count == 0) return;

var redisOptions = new ConfigurationOptions
{
    Password = Environment.GetEnvironmentVariable("RedisOptions__Password"),
    AbortOnConnectFail = true
};
redisOptions.EndPoints.Add(Environment.GetEnvironmentVariable("RedisOptions__Host")
    ?? throw new InvalidOperationException("RedisOptions__Host is required."),
    int.Parse(Environment.GetEnvironmentVariable("RedisOptions__Port") ?? "6379"));
using var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
await redis.GetDatabase().PingAsync();
await using (var transaction = await shops.Database.BeginTransactionAsync())
{
    // EF checks the original ProfileRevision; a concurrent owner/admin write rolls back the whole repair.
    await shops.SaveChangesAsync();
    await transaction.CommitAsync();
}
await ShopProfileCacheInvalidationRequestedHandler.Handle(new ShopProfileCacheInvalidationRequested(planned[0])
    { AdditionalShopIds = planned.Skip(1).ToArray() },
    new RedisService(redis, NullLogger<RedisService>.Instance),
    NullLogger<ShopProfileCacheInvalidationRequestedHandler>.Instance, CancellationToken.None);
var verified = await shops.Shops.AsNoTracking().CountAsync(s => planned.Contains(s.Id) && s.Schedules.Any());
if (verified != planned.Count) throw new InvalidOperationException("Restored schedule count did not match the repair plan.");
Console.WriteLine(JsonSerializer.Serialize(new { restored = verified, cacheInvalidated = true }));
