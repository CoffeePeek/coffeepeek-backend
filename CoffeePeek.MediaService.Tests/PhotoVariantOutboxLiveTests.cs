using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.MediaService.Controllers;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CoffeePeek.Contract.Events.Photo;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Testing;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.MediaService.Tests;

[Trait("Category", LivePostgresServer.Category)]
public sealed class PhotoVariantOutboxLiveTests(MediaVariantsLiveDatabase fixture) : IClassFixture<MediaVariantsLiveDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadyAndEnvelopeRollbackTogetherAndConsumerOnlySeesCommittedManifests()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var probe = new PhotoVariantOutboxProbe(async message =>
        {
            await using var read = fixture.Context();
            return message.IsValid() && await read.PhotoVariantJobs.AnyAsync(j => j.Id == message.JobId && j.Status == PhotoVariantJobStatus.Ready, Ct)
                && await read.PhotoVariants.CountAsync(v => v.JobId == message.JobId, Ct) == 6;
        });
        using var host = Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddScoped(_ => fixture.Context()); services.AddSingleton(probe);
        }).UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(PhotoVariantOutboxLiveTests).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(PhotoVariantOutboxProbeHandler));
            options.UseRuntimeCompilation(); options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
            options.PersistMessagesWithPostgresql(fixture.ConnectionString, "variant_outbox");
            options.Policies.UseDurableLocalQueues(); options.Policies.UseDurableOutboxOnAllSendingEndpoints();
            options.Durability.Mode = DurabilityMode.Solo; options.UseEntityFrameworkCoreTransactions();
        }).Build();
        await host.StartAsync(Ct);
        try
        {
            var photoId = Guid.NewGuid();
            PhotoVariantJob job;
            await using (var seed = fixture.Context())
            {
                var photo = new PhotoMetadata { Id = photoId, CheckInId = Guid.NewGuid(), OwnerId = Guid.NewGuid(),
                    Status = PhotoStatus.Confirmed, BucketType = BucketType.CheckIn, StorageKey = "private/" + photoId,
                    ContentType = "image/jpeg", FileName = "source.jpg", UploadedAt = DateTime.UtcNow };
                seed.Photos.Add(photo); await seed.SaveChangesAsync(Ct);
                await new PhotoVariantQueue(seed).EnqueueAsync(photo, Ct);
                var store = new PhotoVariantJobStore(seed, Options.Create(new PhotoVariantOptions()));
                job = Assert.IsType<PhotoVariantJob>(await store.ClaimAsync(Ct));
                foreach (var variant in PhotoVariantPersistenceTests.Manifest(job)) await store.RecordIntentAsync(job, variant.ObjectKey, Ct);
            }
            await using (var failed = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(new PhotoVariantCommitFailure()).Options))
            {
                using var scope = host.Services.CreateScope();
                var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox>(); outbox.Enroll(failed);
                var store = new PhotoVariantJobStore(failed, Options.Create(new PhotoVariantOptions()), outbox);
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteAsync(job, new string('c', 64), PhotoVariantPersistenceTests.Manifest(job), Ct));
            }
            await using (var check = fixture.Context())
            {
                Assert.Equal(PhotoVariantJobStatus.Processing, (await check.PhotoVariantJobs.SingleAsync(j => j.Id == job.Id, Ct)).Status);
                Assert.False(await check.PhotoVariants.AnyAsync(v => v.PhotoId == photoId, Ct));
                var envelopes = await check.Database.SqlQueryRaw<int>("""
                    SELECT ((SELECT count(*) FROM variant_outbox.wolverine_incoming_envelopes)
                        + (SELECT count(*) FROM variant_outbox.wolverine_outgoing_envelopes))::integer AS "Value"
                    """).SingleAsync(Ct);
                Assert.Equal(0, envelopes);
            }
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
                var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox>(); outbox.Enroll(db);
                var store = new PhotoVariantJobStore(db, Options.Create(new PhotoVariantOptions()), outbox);
                Assert.True(await store.CompleteAsync(job, new string('c', 64), PhotoVariantPersistenceTests.Manifest(job), Ct));
            }
            var observation = await probe.Observations.Reader.ReadAsync(Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(20), Ct);
            Assert.Equal(job.Id, observation.JobId); Assert.True(observation.Committed);
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
                var reads = new PhotoVariantReads(db);
                Assert.NotNull(await reads.FindAsync(job.OwnerId, job.CheckInId, job.PhotoId, job.SourceVersion, 1, "feed", "webp", Ct));
                var replay = new InternalPhotoVariantReconciliationController(db, reads,
                    scope.ServiceProvider.GetRequiredService<IDbContextOutbox>(), Options.Create(new GatewayAuthOptions { SecretKey = "test-replay" }))
                    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
                Assert.IsType<UnauthorizedResult>(await replay.Replay(null, 100, Ct));
                replay.Request.Headers[GatewayHeaderConsts.XGatewayAuth] = "test-replay";
                Assert.IsType<BadRequestResult>(await replay.Replay(null, 101, Ct));
                var result = Assert.IsType<OkObjectResult>(await replay.Replay(null, 100, Ct));
                var page = Assert.IsType<CheckInPhotoVariantReplayResult>(result.Value);
                Assert.Equal(1, page.Published); Assert.Null(page.NextCursor);
            }
            var replayed = await probe.Observations.Reader.ReadAsync(Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(20), Ct);
            Assert.Equal(job.Id, replayed.JobId); Assert.True(replayed.Committed);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
}
