using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CoffeePeek.LegacyCheckInMigration;

public static class LegacyMigrationRunner
{
    public static async Task RunAsync(MigrationSettings settings)
    {
        await using var accountConnection = new NpgsqlConnection(settings.Account);
        // A session advisory lock must not survive in a pooled idle connection after a failed run.
        await using var shopsConnection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(settings.Shops) { Pooling = false }.ConnectionString);
        await using var moderationConnection = new NpgsqlConnection(settings.Moderation);
        await using var mediaConnection = new NpgsqlConnection(settings.Media);
        await accountConnection.OpenAsync();
        await shopsConnection.OpenAsync();
        await moderationConnection.OpenAsync();
        await mediaConnection.OpenAsync();
        var account = new MigrationDatabase(accountConnection);
        var shops = new MigrationDatabase(shopsConnection);
        var moderation = new MigrationDatabase(moderationConnection);
        var media = new MigrationDatabase(mediaConnection);
        // Serializes migration runners. Writers must additionally be stopped by the deploy script.
        await shops.ExecuteAsync("SELECT pg_advisory_lock(76291007)");
        await using var accountContext = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>().UseNpgsql(settings.Account).Options);
        await using var shopsContext = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(settings.Shops).Options);
        await using var moderationContext = new ModerationDbContext(new DbContextOptionsBuilder<ModerationDbContext>().UseNpgsql(settings.Moderation).Options);
        await using var mediaContext = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseNpgsql(settings.Media).Options);
        Console.WriteLine("Preparing canonical schema in four databases.");
        await PrepareAsync(accountContext, "20261007132511_RetireReviewCountOnMain", "20261007160000_RemoveLegacyReviewCounter");
        await PrepareAsync(shopsContext, "20261007133435_CanonicalCheckInsOnMain", "20261007160000_RemoveLegacyCheckInArchives");
        await PrepareAsync(moderationContext, "20261007133453_CanonicalCheckInSnapshotsOnMain", "20261007160000_RemoveLegacyReviewSnapshots");
        await mediaContext.Database.MigrateAsync();

        if (!await shops.IsCompleteAsync() && (await shops.ExistsAsync("Reviews") || await shops.ExistsAsync("CheckIns")))
        {
            var hasPhotos = (await shops.RowsAsync("ShopPhotos")).Any(x => x.Id("CheckInId") != Guid.Empty || x.Id("ReviewId") != Guid.Empty) ||
                (await moderation.RowsAsync("ShopPhotos")).Any(x => x.Id("ModerationReviewId") != Guid.Empty);
            using var storage = hasPhotos ? new MinIOStorageService(Options.Create(settings.Storage), Options.Create(new MediaPublicUrlOptions())) : null;
            Console.WriteLine("Transferring legacy content, moderation, photos, votes and reports.");
            await LegacyDataTransfer.RunAsync(account, shops, moderation, media, storage);
            // These contain only a completion flag, never archived content.
            await account.MarkCompleteAsync();
            await moderation.MarkCompleteAsync();
            await shops.MarkCompleteAsync();
        }
        Console.WriteLine("Removing obsolete tables and columns.");
        await accountContext.Database.MigrateAsync();
        await moderationContext.Database.MigrateAsync();
        await shopsContext.Database.MigrateAsync();
    }

    private static async Task PrepareAsync(DbContext db, string canonical, string cleanup)
    {
        // Never downgrade an already cleaned database on later deployments.
        if (!(await db.Database.GetAppliedMigrationsAsync()).Contains(cleanup))
            await db.GetService<IMigrator>().MigrateAsync(canonical);
    }
}
