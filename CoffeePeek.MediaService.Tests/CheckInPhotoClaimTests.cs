using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class CheckInPhotoClaimTests
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _checkIn = Guid.NewGuid();
    private readonly Mock<IPhotoRepository> _repo = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private PhotoMetadata Photo(string key, BucketType bucket = BucketType.CheckIn) => new()
    {
        Id = Guid.NewGuid(), OwnerId = _owner, FileName = "real.jpg", ContentType = "image/jpeg",
        StorageKey = key, OriginalStorageKey = bucket == BucketType.CheckIn ? key : null,
        SizeBytes = 1, UploadedAt = DateTime.UtcNow, BucketType = bucket,
        Status = PhotoStatus.Pending
    };

    private void Setup(params PhotoMetadata[] photos)
    {
        _repo.Setup(r => r.AcquireClaimLocksAsync(_owner, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        _repo.Setup(r => r.GetClaimPhotosAsync(_owner, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(photos.ToList());
        _repo.Setup(r => r.TryClaimAsync(It.IsAny<Guid>(), _owner, _checkIn, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _storage.Setup(s => s.GetObjectSize(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>())).ReturnsAsync(4096L);
    }

    private Task<CoffeePeek.Shared.Kernel.Response.Response<List<ClaimedCheckInPhotoDto>>> Claim(params string[] keys) =>
        new ClaimCheckInPhotosHandler(_repo.Object, _storage.Object, _uow.Object)
            .ClaimAsync(new ClaimCheckInPhotosRequest(_checkIn, keys), _owner, Ct);

    private static byte[] Cutout(int width = 2, byte backgroundAlpha = 0, byte cupAlpha = 255)
    {
        using var image = new SkiaSharp.SKBitmap(width, 2, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
        image.Erase(new SkiaSharp.SKColor(0, 0, 0, backgroundAlpha));
        image.SetPixel(0, 0, new SkiaSharp.SKColor(120, 60, 20, cupAlpha));
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private void CutoutBytes(byte[] bytes, long? size = null)
    {
        _storage.Setup(s => s.GetObjectSize("cup", BucketType.CheckIn, It.IsAny<CancellationToken>())).ReturnsAsync(size ?? bytes.Length);
        _storage.Setup(s => s.CopyToAsync("cup", BucketType.CheckIn, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((string _, BucketType _, Stream target, CancellationToken ct) => target.WriteAsync(bytes, ct).AsTask());
    }

    private Task<CoffeePeek.Shared.Kernel.Response.Response<List<ClaimedCheckInPhotoDto>>> ClaimWithCutout(string[] keys) =>
        new ClaimCheckInPhotosHandler(_repo.Object, _storage.Object, _uow.Object)
            .ClaimAsync(new(_checkIn, keys, "cup"), _owner, Ct);

    [Fact]
    public async Task TransparentPngCanAccompanyFiveOriginalsAndIsNeverReencoded()
    {
        var originals = Enumerable.Range(0, 5).Select(i => Photo($"original-{i}")).ToArray();
        var cup = Photo("cup"); cup.ContentType = "image/png"; cup.FileName = "cup.png";
        Setup([.. originals, cup]); var bytes = Cutout(); CutoutBytes(bytes);
        var result = await ClaimWithCutout([.. originals.Select(p => p.StorageKey), "cup"]);
        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Data.Count);
        Assert.Equal(bytes.Length, result.Data[^1].SizeBytes);
        _storage.Verify(s => s.Put(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(),
            It.IsAny<BucketType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("opaque")]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("large-dimensions")]
    [InlineData("truncated")]
    [InlineData("fake-png")]
    [InlineData("wrong-mime")]
    [InlineData("size-mismatch")]
    public async Task InvalidCutoutBytesFailClosed(string reason)
    {
        var cup = Photo("cup"); cup.ContentType = reason == "wrong-mime" ? "image/jpeg" : "image/png";
        Setup(Photo("original"), cup);
        var bytes = reason switch
        {
            "opaque" => Cutout(backgroundAlpha: 255),
            "empty" => Cutout(cupAlpha: 0),
            "large-dimensions" => Cutout(width: 769),
            "truncated" => Cutout()[..40],
            "fake-png" => new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 },
            _ => Cutout()
        };
        CutoutBytes(bytes, reason == "oversize" ? 4 * 1024 * 1024 + 1 : reason == "size-mismatch" ? bytes.Length - 1 : null);
        Assert.False((await ClaimWithCutout(["original", "cup"])).IsSuccess);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("expired")]
    [InlineData("claimed")]
    public async Task IneligibleCutoutCannotClaimOriginalEither(string reason)
    {
        var cup = Photo("cup"); cup.ContentType = "image/png";
        if (reason == "foreign") cup.OwnerId = Guid.NewGuid();
        if (reason == "expired") cup.UploadedAt = DateTime.UtcNow.AddDays(-2);
        if (reason == "claimed") cup.CheckInId = Guid.NewGuid();
        Setup(Photo("original"), cup);
        Assert.False((await ClaimWithCutout(["original", "cup"])).IsSuccess);
        _storage.VerifyNoOtherCalls();
        _repo.Verify(r => r.TryClaimAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SixOrdinaryPhotosOrCutoutWithoutOriginalAreRejected()
    {
        Assert.False((await Claim("a", "b", "c", "d", "e", "f")).IsSuccess);
        Assert.False((await ClaimWithCutout(["cup"])).IsSuccess);
        _repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OwnedUploads_PreserveRequestedOrderAndUseActualMetadata()
    {
        var first = Photo("first"); var second = Photo("second"); Setup(first, second);
        var result = await Claim("second", "first");
        result.IsSuccess.Should().BeTrue();
        result.Data!.Select(p => p.PhotoId).Should().Equal(second.Id, first.Id);
        result.Data.Should().OnlyContain(p => p.SizeBytes == 4096 && p.FileName == "real.jpg");
        _storage.Verify(s => s.EnsureCheckInBucketPrivateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LegacyPublicUpload_IsMovedBeforeTheClaimReturns()
    {
        var photo = Photo("reviews/old", BucketType.Shop); Setup(photo);
        _storage.Setup(s => s.MoveToCheckInStorageAsync("reviews/old", photo.Id, It.IsAny<CancellationToken>(), BucketType.Shop))
            .ReturnsAsync("check-ins/private");
        var result = await Claim("reviews/old");
        result.Data!.Single().StorageKey.Should().Be("check-ins/private");
        result.Data.Single().RequestedStorageKey.Should().Be("reviews/old");
        photo.BucketType.Should().Be(BucketType.CheckIn);
        photo.OriginalStorageKey.Should().Be("reviews/old");
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("used")]
    [InlineData("expired")]
    [InlineData("claimed")]
    public async Task IneligibleUploads_AreRejectedBeforeStorageAccess(string reason)
    {
        var photo = Photo("photo");
        if (reason == "foreign") photo.OwnerId = Guid.NewGuid();
        if (reason == "used") photo.Status = PhotoStatus.Confirmed;
        if (reason == "expired") photo.UploadedAt = DateTime.UtcNow.AddDays(-2);
        if (reason == "claimed") photo.CheckInId = Guid.NewGuid();
        Setup(photo);
        (await Claim("photo")).IsSuccess.Should().BeFalse();
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompetingClaim_FailsClosedWithoutReleasingAnotherAttempt()
    {
        var photo = Photo("photo"); Setup(photo);
        _repo.Setup(r => r.TryClaimAsync(photo.Id, _owner, _checkIn, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await FluentActions.Awaiting(() => Claim("photo")).Should().ThrowAsync<InvalidOperationException>();
        _repo.Verify(r => r.ReleaseClaimAsync(_owner, _checkIn, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SameConfirmedCheckIn_RetryKeepsTheBinding()
    {
        var photo = Photo("photo"); photo.CheckInId = _checkIn; photo.Status = PhotoStatus.Confirmed; Setup(photo);
        (await Claim("photo")).IsSuccess.Should().BeTrue();
        photo.CheckInId.Should().Be(_checkIn);
        photo.Status.Should().Be(PhotoStatus.Confirmed);
    }

    [Fact]
    public async Task MissingActualObject_FailsClosed()
    {
        Setup(Photo("photo"));
        _storage.Setup(s => s.GetObjectSize("photo", BucketType.CheckIn, It.IsAny<CancellationToken>())).ReturnsAsync((long?)null);
        await FluentActions.Awaiting(() => Claim("photo")).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FailedSameIdRetryCannotReleaseTheWinnersPendingReservation()
    {
        var photo=Photo("photo");photo.CheckInId=_checkIn;photo.Status=PhotoStatus.Pending;Setup(photo);
        _storage.Setup(s=>s.GetObjectSize("photo",BucketType.CheckIn,It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        await FluentActions.Awaiting(()=>Claim("photo")).Should().ThrowAsync<OperationCanceledException>();
        photo.CheckInId.Should().Be(_checkIn);photo.Status.Should().Be(PhotoStatus.Pending);
        _repo.Verify(r=>r.ReleaseClaimAsync(_owner,_checkIn,It.IsAny<CancellationToken>()),Times.Never);
    }

    [Fact]
    public async Task FreshPrivateUpload_RotatesTheKeyToPreventChangesThroughThePutUrl()
    {
        var photo = Photo("upload-key"); photo.OriginalStorageKey = null; Setup(photo);
        _storage.Setup(s => s.MoveToCheckInStorageAsync("upload-key", photo.Id, It.IsAny<CancellationToken>(), BucketType.CheckIn))
            .ReturnsAsync("immutable-key");
        var result = await Claim("upload-key");
        result.Data!.Single().StorageKey.Should().Be("immutable-key");
    }

    [Fact]
    public async Task AmbiguousHistoricalLocator_IsRejectedWithoutMovingEitherObject()
    {
        var first=Photo("private-first"); first.OriginalStorageKey="old-key";
        var second=Photo("private-second"); second.OriginalStorageKey="old-key";
        Setup(first, second);
        (await Claim("old-key")).IsSuccess.Should().BeFalse();
        _storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    public async Task UnsafeHistoricalMime_CannotBeClaimedAsANewPhoto(string contentType)
    {
        var photo=Photo("unsafe"); photo.ContentType=contentType; Setup(photo);
        (await Claim("unsafe")).IsSuccess.Should().BeFalse();
        _storage.VerifyNoOtherCalls();
        _repo.Verify(r => r.TryClaimAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OverlappingSameIdRetry_WaitsForFinalSaveThenReloadsTheImmutableDestination()
    {
        var photo = Photo("upload"); photo.OriginalStorageKey = null; Setup(photo);
        using var gate = new SemaphoreSlim(1, 1);
        var finalSaveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFinalSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitingRetry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookupCount = 0; var leaseCount = 0; var saves = 0; var committed = false; var active = false;
        var sourceBytes = "original"; string? privateBytes = null;
        _repo.Setup(r => r.AcquireClaimLocksAsync(_owner, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (Interlocked.Increment(ref leaseCount) == 2) waitingRetry.SetResult();
                await gate.WaitAsync(Ct); active = true;
                return new TestClaimLease(() => { active = false; gate.Release(); });
            });
        _repo.Setup(r => r.GetClaimPhotosAsync(_owner, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                active.Should().BeTrue(); Interlocked.Increment(ref lookupCount);
                var fresh = Photo(committed ? "immutable" : "upload"); fresh.Id = photo.Id;
                fresh.OriginalStorageKey = committed ? "upload" : null;
                fresh.CheckInId = committed ? _checkIn : null;
                return Task.FromResult(new List<PhotoMetadata> { fresh });
            });
        _storage.Setup(s => s.MoveToCheckInStorageAsync("upload", photo.Id, It.IsAny<CancellationToken>(), BucketType.CheckIn))
            .ReturnsAsync(() => { privateBytes = sourceBytes; return "immutable"; });
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(async () =>
        {
            active.Should().BeTrue();
            if (Interlocked.Increment(ref saves) == 2)
            {
                finalSaveEntered.SetResult(); await finishFinalSave.Task.WaitAsync(Ct); committed = true;
            }
            return 1;
        });
        var winner = Claim("upload"); await finalSaveEntered.Task.WaitAsync(Ct);
        sourceBytes = "changed through live PUT";
        var retry = Claim("upload"); await waitingRetry.Task.WaitAsync(Ct);
        lookupCount.Should().Be(1); retry.IsCompleted.Should().BeFalse();
        finishFinalSave.SetResult();
        var responses = await Task.WhenAll(winner, retry);
        responses.Should().OnlyContain(r => r.IsSuccess && r.Data!.Single().StorageKey == "immutable");
        lookupCount.Should().Be(2); active.Should().BeFalse(); privateBytes.Should().Be("original");
        _storage.Verify(s => s.MoveToCheckInStorageAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>(), It.IsAny<BucketType>()), Times.Once);
        _repo.Verify(r => r.ReleaseClaimAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class TestClaimLease(Action dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { dispose(); return ValueTask.CompletedTask; }
    }
}
