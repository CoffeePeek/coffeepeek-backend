using CoffeePeek.Contract.Dtos.Ownership;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.MediaService.Tests;
public class OwnershipEvidenceAccessTests
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly Guid claim = Guid.NewGuid();
    private readonly Mock<IPhotoRepository> photos = new();
    private readonly Mock<IStorageService> storage = new();
    private readonly Mock<IUnitOfWork> unit = new();
    private readonly HashSet<(Guid PhotoId, Guid ClaimId)> bindings = [];
    private InternalOwnershipEvidenceController Controller(bool trusted = true)
    {
        var user = new Mock<IUserContext>(); user.Setup(x => x.GetUserId()).Returns(owner);
        var controller = new InternalOwnershipEvidenceController(photos.Object, storage.Object, unit.Object,
            user.Object, Options.Create(new GatewayAuthOptions { SecretKey = "test-only" }));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.Response.Body = new MemoryStream();
        if (trusted) controller.Request.Headers[GatewayHeaderConsts.XGatewayAuth] = "test-only";
        return controller;
    }
    private PhotoMetadata Photo() => new() { Id = Guid.NewGuid(), OwnerId = owner,
        OwnerType = OwnerType.OwnershipEvidence, BucketType = BucketType.CheckIn, ContentType = "image/png",
        StorageKey = "ownership-evidence/test", FileName = "proof.png", Status = PhotoStatus.Pending, UploadedAt = DateTime.UtcNow };
    private void Setup(PhotoMetadata photo)
    {
        photos.Setup(x => x.HasEvidenceBindingAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid photoId, Guid claimId, CancellationToken _) => bindings.Contains((photoId, claimId)));
        photos.Setup(x => x.AddEvidenceBinding(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .Callback((Guid photoId, Guid claimId) => Assert.True(bindings.Add((photoId, claimId))));
        photos.Setup(x => x.GetByIdAsync(photo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(photo);
        photos.Setup(x => x.AcquireClaimLocksAsync(owner, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        photos.Setup(x => x.GetClaimPhotosAsync(owner, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync([photo]);
        storage.Setup(x => x.GetObjectSize(photo.StorageKey, BucketType.CheckIn, It.IsAny<CancellationToken>())).ReturnsAsync(64L);
    }
    [Fact]
    public async Task OnlyTrustedServiceCallsCanAttachOrRead()
    {
        var controller = Controller(false);
        Assert.IsType<UnauthorizedResult>(await controller.Attach(new(claim, [Guid.NewGuid()]), CancellationToken.None));
        Assert.IsType<UnauthorizedResult>(await controller.Download(claim, Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(photos.Invocations);
    }
    [Fact]
    public async Task ForeignUploadsAndOtherPhotoPurposesCannotBecomeEvidence()
    {
        var photo = Photo(); photo.OwnerId = Guid.NewGuid(); Setup(photo);
        Assert.IsType<BadRequestResult>(await Controller().Attach(new(claim, [photo.Id]), CancellationToken.None));
        photo.OwnerId = owner; photo.OwnerType = OwnerType.Shop;
        Assert.IsType<BadRequestResult>(await Controller().Attach(new(claim, [photo.Id]), CancellationToken.None));
        unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task AttachIsIdempotentForItsClaimAndRejectsAnotherClaim()
    {
        var photo = Photo(); Setup(photo);
        Assert.IsType<NoContentResult>(await Controller().Attach(new(claim, [photo.Id]), CancellationToken.None));
        Assert.Equal(PhotoStatus.Confirmed, photo.Status);
        Assert.IsType<NoContentResult>(await Controller().Attach(new(claim, [photo.Id]), CancellationToken.None));
        Assert.IsType<ConflictResult>(await Controller().Attach(new(Guid.NewGuid(), [photo.Id]), CancellationToken.None));
    }

    [Fact]
    public async Task ReusedEvidencePreservesBothClaimsAndRejectsAnUnrelatedReservation()
    {
        var photo = Photo(); Setup(photo);
        Assert.IsType<NoContentResult>(await Controller().Attach(new(claim, [photo.Id]), CancellationToken.None));
        var next = Guid.NewGuid();
        Assert.IsType<ConflictResult>(await Controller().Attach(new(next, [photo.Id], Guid.NewGuid()), CancellationToken.None));
        Assert.IsType<NoContentResult>(await Controller().Attach(new(next, [photo.Id], claim), CancellationToken.None));
        Assert.IsType<NoContentResult>(await Controller().Attach(new(next, [photo.Id], claim), CancellationToken.None));
        Assert.Equal(claim, photo.OwnershipClaimId);
        Assert.Equal(2, bindings.Count);
        Assert.IsType<EmptyResult>(await Controller().Download(claim, photo.Id, CancellationToken.None));
        Assert.IsType<EmptyResult>(await Controller().Download(next, photo.Id, CancellationToken.None));
        var third = Guid.NewGuid();
        Assert.IsType<NoContentResult>(await Controller().Attach(new(third, [photo.Id], next), CancellationToken.None));
        Assert.IsType<EmptyResult>(await Controller().Download(third, photo.Id, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Controller().Download(Guid.NewGuid(), photo.Id, CancellationToken.None));
    }

    [Fact]
    public async Task APhotoReservedForAShelfBagCannotBecomeEvidence()
    {
        var photo = Photo();
        photo.ShelfBagId = Guid.NewGuid();
        Setup(photo);

        Assert.IsType<ConflictResult>(await Controller().Attach(new(claim, [photo.Id]), CancellationToken.None));

        unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(x => x.MarkAsPermanent(It.IsAny<string>(), It.IsAny<BucketType>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [Fact]
    public async Task UnattachedForeignAndWrongClaimPhotosAreNotReadable()
    {
        var photo = Photo(); Setup(photo);
        Assert.IsType<NotFoundResult>(await Controller().Download(claim, photo.Id, CancellationToken.None));
        photo.OwnershipClaimId = claim; photo.Status = PhotoStatus.Confirmed;
        Assert.IsType<NotFoundResult>(await Controller().Download(Guid.NewGuid(), photo.Id, CancellationToken.None));
        photo.OwnerId = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await Controller().Download(claim, photo.Id, CancellationToken.None));
    }
    [Fact]
    public async Task EvidenceCanNeverBeClaimedAsACheckIn()
    {
        var photo = Photo(); Setup(photo);
        var result = await new CoffeePeek.MediaService.Handlers.ClaimCheckInPhotosHandler(photos.Object, storage.Object, unit.Object)
            .ClaimAsync(new(claim, [photo.StorageKey]), owner, CancellationToken.None);
        Assert.False(result.IsSuccess);
        photos.Verify(x => x.TryClaimAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
