using CoffeePeek.MediaService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CoffeePeek.MediaService.Tests;

public sealed class ShopPhotoMigrationTests
{
    [Fact]
    public void MediaModel_MatchesCommittedMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var db = new MediaDbContext(options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Migration_CreatesDurableAttachmentColumnsAndPairConstraint()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var db = new MediaDbContext(options);

        var sql = db.GetService<IMigrator>().GenerateScript(
            "20261007132516_CanonicalCheckInPhotoClaimsOnMain",
            "20261008211500_ReserveShopPhotoAttachments");

        Assert.Contains("ShopAttachmentId", sql, StringComparison.Ordinal);
        Assert.Contains("ShopAttachmentKind", sql, StringComparison.Ordinal);
        Assert.Contains("CK_Photos_ShopAttachmentPair", sql, StringComparison.Ordinal);
    }
}
