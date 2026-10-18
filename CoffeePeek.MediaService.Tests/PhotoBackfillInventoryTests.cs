using System.Security.Cryptography;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services.Backfill;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Shared.Kernel.Media;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoBackfillInventoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static MediaDbContext Context() => new(new DbContextOptionsBuilder<MediaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Theory]
    [InlineData("valid", null)]
    [InlineData("upload", "provenance_unverified")]
    [InlineData("public", "needs_transfer")]
    [InlineData("owner", "binding_mismatch")]
    [InlineData("parent", "binding_mismatch")]
    [InlineData("key", "binding_mismatch")]
    [InlineData("shop_attachment", "binding_mismatch")]
    [InlineData("pending", "media_not_confirmed")]
    [InlineData("deleted", "deleted")]
    [InlineData("missing", "media_missing")]
    [InlineData("source", "invalid_candidate")]
    public void EligibilityRequiresCurrentPrivateImmutableBinding(string mutation, string? expected)
    {
        var (candidate, photo) = Binding();
        if (mutation == "upload") { photo.StorageKey = "check-ins/" + Guid.NewGuid() + ".png"; candidate = Candidate(photo); }
        if (mutation == "public") photo.BucketType = BucketType.Shop;
        if (mutation == "owner") photo.OwnerId = Guid.NewGuid();
        if (mutation == "parent") photo.CheckInId = Guid.NewGuid();
        if (mutation == "key") photo.StorageKey += ".other";
        if (mutation == "shop_attachment") photo.ShopAttachmentId = Guid.NewGuid();
        if (mutation == "pending") photo.Status = PhotoStatus.Pending;
        if (mutation == "source") candidate = candidate with { SourceVersion = new string('b', 64) };
        Assert.Equal(expected, PhotoBackfillEligibility.Evaluate(candidate, mutation == "missing" ? null : photo, mutation == "deleted"));
    }

    [Fact]
    public async Task IneligibleAndDeletedRowsNeverOpenStorageAndInspectionDoesNotWriteOrTrack()
    {
        await using var db = Context();
        var first = Binding(); var second = Binding(); second.Photo.BucketType = BucketType.Shop;
        db.Photos.AddRange(first.Photo, second.Photo);
        db.CheckInPhotoTombstones.Add(new CheckInPhotoTombstone { CheckInId = first.Candidate.CheckInId, DeletedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var reader = new Reader((_, _) => throw new InvalidOperationException("Storage must not be called."));
        var inspector = new PhotoBackfillInspector(db, reader);
        var result = await inspector.InspectAsync([first.Candidate, second.Candidate], true, Ct);
        Assert.Equal(new[] { "deleted", "needs_transfer" }, result.Select(x => x.EligibilityFailure));
        Assert.All(result, x => Assert.Equal("not_checked", x.SourceStatus)); Assert.Equal(0, reader.Calls);
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(2, await db.Photos.CountAsync(Ct));
        Assert.Empty(await db.PhotoVariantJobs.ToArrayAsync(Ct)); Assert.Empty(await db.PhotoVariantArtifacts.ToArrayAsync(Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullDecodeAcceptsOpaqueAndTransparentPngAndMeasuresActualLengthAndDigest(bool alpha)
    {
        var bytes = Image(alpha); await using var source = new PhotoBackfillObject(new MemoryStream(bytes), null);
        var result = await PhotoBackfillContentInspector.InspectAsync(source, true, Ct);
        Assert.Equal("verified", result.Status); Assert.Equal(bytes.LongLength, result.Bytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.Digest);
    }

    [Theory]
    [InlineData("truncated", "corrupt")]
    [InlineData("unknown", "unsupported")]
    [InlineData("empty", "corrupt")]
    [InlineData("wrong_length", "length_mismatch")]
    [InlineData("too_large", "oversized")]
    public async Task ActualDecodeDistinguishesCorruptionUnsupportedMismatchAndLimits(string input, string expected)
    {
        var bytes = Image(true);
        if (input == "truncated") bytes = bytes[..(bytes.Length / 2)];
        if (input == "unknown") bytes = "unknown file type"u8.ToArray();
        if (input == "empty") bytes = [];
        await using var source = new PhotoBackfillObject(new MemoryStream(bytes), input switch
        { "wrong_length" => bytes.Length + 1, "too_large" => PhotoVariantOptions.MaxSourceBytes + 1L, _ => null });
        var result = await PhotoBackfillContentInspector.InspectAsync(source, true, Ct);
        Assert.Equal(expected, result.Status); Assert.Null(result.Digest);
    }

    [Fact]
    public async Task UnknownLengthSourceStopsAtByteCapWithoutDecodingOrReadingToEnd()
    {
        await using var stream = new RepeatingStream(PhotoVariantOptions.MaxSourceBytes + 8L * 1024 * 1024);
        await using var source = new PhotoBackfillObject(stream, null);
        var result = await PhotoBackfillContentInspector.InspectAsync(source, true, Ct);
        Assert.Equal("oversized", result.Status); Assert.Null(result.Digest);
        Assert.InRange(stream.ReadBytes, PhotoVariantOptions.MaxSourceBytes + 1L, PhotoVariantOptions.MaxSourceBytes + 64L * 1024);
    }

    [Fact]
    public async Task PixelBombIsRejectedFromHeaderBeforeAllocatingDecodedPixels()
    {
        var bytes = Image(false);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 40_001);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), 1_001);
        // Repair only the IHDR checksum; its tiny IDAT deliberately cannot describe this raster.
        uint crc = uint.MaxValue;
        foreach (var b in bytes.AsSpan(12, 17))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), ~crc);
        await using var source = new PhotoBackfillObject(new MemoryStream(bytes), bytes.Length);
        var result = await PhotoBackfillContentInspector.InspectAsync(source, true, Ct);
        Assert.Equal("oversized", result.Status); Assert.Null(result.Digest);
    }

    [Fact]
    public async Task MetadataOnlyDoesNotReadBodyAndNeverClaimsIntegrity()
    {
        await using var source = new PhotoBackfillObject(new NeverReadStream(), 123);
        var result = await PhotoBackfillContentInspector.InspectAsync(source, false, Ct);
        Assert.Equal("unverified", result.Status); Assert.Equal(123, result.Bytes); Assert.Null(result.Digest);
    }

    [Theory]
    [InlineData("missing", "missing")]
    [InlineData("403", "unavailable")]
    [InlineData("timeout", "unavailable")]
    public async Task StorageFailuresDoNotBecomeCorruptionOrMissingExceptConfirmedObjectAbsence(string failure, string expected)
    {
        await using var db = Context(); var binding = Binding(); db.Photos.Add(binding.Photo); await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var reader = new Reader((_, _) => failure switch
        {
            "missing" => Task.FromException<PhotoBackfillObject>(new PhotoBackfillObjectMissingException()),
            "403" => Task.FromException<PhotoBackfillObject>(new HttpRequestException("403 private-key signed-secret")),
            _ => Task.FromException<PhotoBackfillObject>(new TaskCanceledException("Storage timeout private-key"))
        });
        var result = Assert.Single(await new PhotoBackfillInspector(db, reader).InspectAsync([binding.Candidate], true, Ct));
        Assert.Equal(expected, result.SourceStatus); Assert.Null(result.EligibilityFailure);
        Assert.DoesNotContain("private-key", System.Text.Json.JsonSerializer.Serialize(result)); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("complete", true)]
    [InlineData("partial", false)]
    [InlineData("wrong_recipe", false)]
    [InlineData("wrong_digest", false)]
    [InlineData("wrong_variant_binding", false)]
    [InlineData("failed", false)]
    public async Task OnlyCompleteCurrentReadyManifestWithMatchingSourceContributesMeasuredVariantBytes(string mutation, bool hasSample)
    {
        await using var db = Context(); var bytes = Image(true); var binding = Binding(bytes.Length); db.Photos.Add(binding.Photo);
        var job = new PhotoVariantJob
        {
            Id = Guid.NewGuid(), PhotoId = binding.Photo.Id, CheckInId = binding.Candidate.CheckInId, OwnerId = binding.Photo.OwnerId,
            SourceKey = binding.Photo.StorageKey, SourceVersion = binding.Candidate.SourceVersion, RecipeVersion = PhotoVariantRecipe.Version,
            Status = PhotoVariantJobStatus.Ready, SourceDigest = Convert.ToHexStringLower(SHA256.HashData(bytes))
        };
        var variants = PhotoVariantRecipe.Sizes.Keys.SelectMany(p => new[] { "webp", "png" }.Select(f => new PhotoVariant
        {
            Id = Guid.NewGuid(), JobId = job.Id, PhotoId = job.PhotoId, SourceVersion = job.SourceVersion,
            RecipeVersion = job.RecipeVersion, Purpose = p, Format = f, ContentType = "image/" + f,
            Width = 20, Height = 30, SizeBytes = 123, Digest = new string('a', 64), ObjectKey = "never-read/" + Guid.NewGuid()
        })).ToArray();
        if (mutation == "partial") variants = variants[..5];
        if (mutation == "wrong_recipe") job.RecipeVersion++;
        if (mutation == "wrong_digest") job.SourceDigest = new string('b', 64);
        if (mutation == "wrong_variant_binding") variants[0].PhotoId = Guid.NewGuid();
        if (mutation == "failed") job.Status = PhotoVariantJobStatus.Failed;
        db.PhotoVariantJobs.Add(job); db.PhotoVariants.AddRange(variants); await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var reader = new Reader((_, _) => Task.FromResult(new PhotoBackfillObject(new MemoryStream(bytes), bytes.Length)));
        var result = Assert.Single(await new PhotoBackfillInspector(db, reader).InspectAsync([binding.Candidate], true, Ct));
        Assert.Equal(hasSample ? 6L * 123 : null, result.CurrentVariantBytes);
        Assert.Equal(mutation == "wrong_digest" ? "source_changed" : "verified", result.SourceStatus);
        Assert.Equal(PhotoVariantRecipe.Version, result.RecipeVersion); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task MetadataMismatchIsReportedFromCommunityMediaAndActualObjectMeasurements()
    {
        await using var db = Context(); var bytes = Image(false); var binding = Binding(bytes.Length + 2);
        db.Photos.Add(binding.Photo); await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var candidate = binding.Candidate with { DeclaredSizeBytes = bytes.Length + 1, ContentType = "image/jpeg" };
        var result = Assert.Single(await new PhotoBackfillInspector(db,
            new Reader((_, _) => Task.FromResult(new PhotoBackfillObject(new MemoryStream(bytes), null))))
            .InspectAsync([candidate], true, Ct));
        Assert.True(result.MetadataMismatch); Assert.Equal(bytes.LongLength, result.ActualBytes);
        Assert.Equal(bytes.Length + 2, result.MediaDeclaredBytes); Assert.Equal(bytes.Length + 1, result.DeclaredBytes);
    }

    [Fact]
    public async Task ConcurrentScopesShareOneInspectionSlotAndQueuedCancellationDoesNotOpenStorage()
    {
        await using var db1 = Context(); await using var db2 = Context(); var first = Binding(); var second = Binding();
        db1.Photos.Add(first.Photo); db2.Photos.Add(second.Photo); await db1.SaveChangesAsync(Ct); await db2.SaveChangesAsync(Ct);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader1 = new Reader(async (_, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return new PhotoBackfillObject(new MemoryStream(Image(false)), null); });
        var reader2 = new Reader((_, _) => Task.FromResult(new PhotoBackfillObject(new MemoryStream(Image(false)), null)));
        var running = new PhotoBackfillInspector(db1, reader1).InspectAsync([first.Candidate], true, Ct);
        await entered.Task.WaitAsync(Ct);
        using var queued = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        try
        {
            var waiting = new PhotoBackfillInspector(db2, reader2).InspectAsync([second.Candidate], true, queued.Token);
            Assert.False(waiting.IsCompleted); Assert.Equal(0, reader2.Calls); queued.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting); Assert.Equal(0, reader2.Calls);
        }
        finally { release.TrySetResult(); }
        Assert.Equal("verified", Assert.Single(await running).SourceStatus);
        Assert.Equal("verified", Assert.Single(await new PhotoBackfillInspector(db2, reader2).InspectAsync([second.Candidate], true, Ct)).SourceStatus);
    }

    internal static (PhotoBackfillCandidate Candidate, PhotoMetadata Photo) Binding(long length = 123)
    {
        var photo = new PhotoMetadata { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), CheckInId = Guid.NewGuid(),
            BucketType = BucketType.CheckIn, Status = PhotoStatus.Confirmed, ContentType = "image/png", FileName = "source.png", SizeBytes = length };
        photo.StorageKey = $"check-ins/{photo.Id:N}.png"; return (Candidate(photo), photo);
    }

    private static PhotoBackfillCandidate Candidate(PhotoMetadata photo) => new(photo.Id, photo.CheckInId!.Value,
        photo.OwnerId, photo.StorageKey, CheckInPhotoVariantIdentity.SourceVersion(photo.Id, photo.StorageKey), CheckInKind.Home,
        true, DateTime.UtcNow.AddDays(-1), photo.SizeBytes, photo.ContentType);

    private static byte[] Image(bool alpha)
    {
        using var bitmap = new SKBitmap(8, 12); bitmap.Erase(alpha ? new SKColor(60, 80, 120, 100) : SKColors.Brown);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }

    private sealed class Reader(Func<string, CancellationToken, Task<PhotoBackfillObject>> open) : IPhotoBackfillObjectReader
    {
        public int Calls { get; private set; }
        public Task<PhotoBackfillObject> OpenAsync(string key, CancellationToken ct) { Calls++; return open(key, ct); }
    }

    private sealed class NeverReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => throw new InvalidOperationException("Metadata-only inspection must not read the body.");
    }

    private sealed class RepeatingStream(long bytes) : Stream
    {
        public long ReadBytes { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); var count = (int)Math.Min(buffer.Length, bytes - ReadBytes); buffer.Span[..count].Fill(7); ReadBytes += count; return ValueTask.FromResult(count); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => ReadBytes; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
