using CoffeePeek.MediaService.Consumers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Events;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.MediaService.Services.Variants;
using CoffeePeek.Shared.Kernel;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public sealed class DeletePhotoVariantsTests
{
    [Fact]
    public async Task RedeliveredSourceDeletionCancelsVariantsEvenIfOriginalWasAlreadyDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var photo = new PhotoMetadata { Id = Guid.NewGuid(), Status = PhotoStatus.Deleted };
        var repository = new Mock<IPhotoRepository>();
        repository.Setup(x => x.GetByIdAsync(photo.Id, ct)).ReturnsAsync(photo);
        var storage = new Mock<IStorageService>();
        var queue = new Mock<IPhotoVariantQueue>();
        await new DeletePhotoHandler(repository.Object, Mock.Of<IUnitOfWork>(), storage.Object, queue.Object)
            .Handle(new DeletePhotoFromStorageEvent(photo.Id, "ignored"), ct);
        queue.Verify(x => x.CancelPhotoAsync(photo.Id, ct), Times.Once);
        storage.VerifyNoOtherCalls();
    }
}
