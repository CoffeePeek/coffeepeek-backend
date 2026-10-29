using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace CoffeePeek.MediaService.Tests;

[Trait("Category", LivePostgresServer.Category)]
public sealed class OwnershipEvidenceBindingLiveTests(MediaVariantsLiveDatabase fixture) : IClassFixture<MediaVariantsLiveDatabase>
{
    [Fact]
    public async Task LegacyReservationAndNewAttemptRemainReadableAndSurviveRejectedDowngrade()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid(); var previous = Guid.NewGuid(); var next = Guid.NewGuid();
        await using var db = fixture.Context();
        var photo = PhotoMetadata.Pending("proof.png", "image/png", $"ownership-evidence/{Guid.NewGuid():N}",
            64, BucketType.CheckIn, OwnerType.OwnershipEvidence, owner);
        photo.OwnershipClaimId = previous;
        photo.Status = PhotoStatus.Confirmed;
        db.Photos.Add(photo);
        await db.SaveChangesAsync(ct);
        Assert.IsType<ConflictResult>(await Controller(db, owner).Attach(new(next, [photo.Id]), ct));
        Assert.IsType<NoContentResult>(await Controller(db, owner).Attach(new(next, [photo.Id], previous), ct));
        Assert.IsType<NoContentResult>(await Controller(db, owner).Attach(new(next, [photo.Id], previous), ct));
        db.ChangeTracker.Clear();
        Assert.Equal(previous, (await db.Photos.SingleAsync(p => p.Id == photo.Id, ct)).OwnershipClaimId);
        Assert.Single(await db.OwnershipEvidenceBindings.Where(b => b.PhotoId == photo.Id && b.ClaimId == next).ToArrayAsync(ct));
        Assert.IsType<EmptyResult>(await Controller(db, owner).Download(previous, photo.Id, ct));
        Assert.IsType<EmptyResult>(await Controller(db, owner).Download(next, photo.Id, ct));
        Assert.IsType<NotFoundResult>(await Controller(db, Guid.NewGuid()).Download(next, photo.Id, ct));
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.GetService<IMigrator>().MigrateAsync("20261029083142_AddOwnershipEvidencePhotos", ct));
        Assert.Contains("Ownership evidence history exists", error.MessageText);
        Assert.True(await db.OwnershipEvidenceBindings.AnyAsync(b => b.PhotoId == photo.Id && b.ClaimId == next, ct));
    }

    private static InternalOwnershipEvidenceController Controller(MediaDbContext db, Guid owner)
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(x => x.GetObjectSize(It.IsAny<string>(), BucketType.CheckIn, It.IsAny<CancellationToken>())).ReturnsAsync(64L);
        var user = Mock.Of<IUserContext>(x => x.GetUserId() == owner);
        var controller = new InternalOwnershipEvidenceController(new PhotoRepository(db), storage.Object,
            new UnitOfWork<MediaDbContext>(db), user, Options.Create(new GatewayAuthOptions { SecretKey = "test-only" }));
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() };
        controller.Request.Headers[GatewayHeaderConsts.XGatewayAuth] = "test-only";
        controller.Response.Body = new MemoryStream();
        return controller;
    }
}
