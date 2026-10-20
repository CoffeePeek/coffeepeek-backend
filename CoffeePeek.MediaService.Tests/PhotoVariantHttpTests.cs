using System.Net;
using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoVariantHttpTests
{
    [Fact]
    public async Task RealHttpKeepsRepresentationLengthOnHeadAnd304AndNeverWritesTheirBody()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ApplicationName = typeof(InternalCheckInPhotoVariantsController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddControllers();
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<MediaDbContext>(options => options.UseInMemoryDatabase(database));
        builder.Services.AddScoped<PhotoVariantReads>();
        var state = new PhotoVariantReadState(TimeProvider.System); state.Lease.MarkVerified(TimeProvider.System.GetTimestamp());
        builder.Services.AddSingleton(state);
        builder.Services.AddSingleton<IOptions<PhotoVariantOptions>>(Options.Create(new PhotoVariantOptions { ReadEnabled = true }));
        builder.Services.AddSingleton<IOptions<GatewayAuthOptions>>(Options.Create(new GatewayAuthOptions { SecretKey = "http-test-secret" }));
        var owner = Guid.NewGuid(); var checkIn = Guid.NewGuid(); var photoId = Guid.NewGuid();
        var user = new Mock<IUserContext>(); user.Setup(u => u.GetUserId()).Returns(owner); builder.Services.AddSingleton(user.Object);
        var reader = new Mock<IPhotoVariantReader>();
        reader.Setup(r => r.CopyToAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<string, Stream, CancellationToken>((_, stream, token) => stream.WriteAsync(new byte[123], token).AsTask());
        builder.Services.AddSingleton(reader.Object);
        await using var app = builder.Build(); app.MapControllers();
        var source = CheckInPhotoVariantIdentity.SourceVersion(photoId, "original");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
            db.Photos.Add(new PhotoMetadata { Id = photoId, OwnerId = owner, CheckInId = checkIn, StorageKey = "original",
                FileName = "original.jpg", ContentType = "image/jpeg", Status = PhotoStatus.Confirmed, BucketType = BucketType.CheckIn });
            var job = new PhotoVariantJob { Id = Guid.NewGuid(), OwnerId = owner, CheckInId = checkIn, PhotoId = photoId,
                SourceKey = "original", SourceVersion = source, RecipeVersion = 1, Status = PhotoVariantJobStatus.Ready, LeaseToken = Guid.NewGuid() };
            db.PhotoVariantJobs.Add(job); db.PhotoVariants.AddRange(PhotoVariantPersistenceTests.Manifest(job)); await db.SaveChangesAsync(ct);
        }
        await app.StartAsync(ct);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.Add(GatewayHeaderConsts.XGatewayAuth, "http-test-secret");
            var path = $"/internal/check-in-photo-variants/{checkIn}/{photoId}/{source}/1/feed.webp";
            using var get = await client.GetAsync(path, ct);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(123, (await get.Content.ReadAsByteArrayAsync(ct)).Length);
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
            {
                using var request = new HttpRequestMessage(method, path);
                request.Headers.IfNoneMatch.Add(get.Headers.ETag!);
                using var response = await client.SendAsync(request, ct);
                Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
                Assert.Equal(123, response.Content.Headers.ContentLength);
                Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
                Assert.Equal(get.Headers.ETag, response.Headers.ETag);
                Assert.Empty(await response.Content.ReadAsByteArrayAsync(ct));
                Assert.True(response.Headers.CacheControl?.NoStore);
            }
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, path);
            using var head = await client.SendAsync(headRequest, ct);
            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
            Assert.Equal(123, head.Content.Headers.ContentLength);
            Assert.Empty(await head.Content.ReadAsByteArrayAsync(ct));
            reader.Verify(r => r.CopyToAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally { await app.StopAsync(CancellationToken.None); }
    }
}
