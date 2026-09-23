using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Domain.Entities.PhotoMetadataAggregate;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Infrastructure.Consumers;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Account.Infrastructure.Tests.Consumers;

public class UserPhotoUploadedHandlerTests
{
    [Fact]
    public async Task Handle_AddsPhotoMetadataBeforeLinkingItToTheUser()
    {
        // The handler used to only call user.UpdateAvatar(photo) and never registered the
        // new PhotoMetadata with the repository, relying on EF's cascade tracking to insert it.
        // In production this occasionally left User.PhotoMetadataId pointing at a row that was
        // never persisted, violating FK_Users_Photos_PhotoMetadataId (CP-ACCOUNT-SERVICE-3A).
        var userRepo = new Mock<IUserRepository>();
        var photoRepo = new Mock<IPhotoMetadataRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var user = User.Register("user@example.com", "testuser", "hash", Role.Create("User"));
        var message = new PhotoUploadedEvent(
            Guid.NewGuid(),
            "storage-key",
            "avatar.png",
            "image/png",
            1024,
            "User",
            user.Id,
            DateTime.UtcNow);

        userRepo.Setup(r => r.GetById(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var sut = new UserPhotoUploadedHandler(userRepo.Object, photoRepo.Object, unitOfWork.Object);

        await sut.Handle(message, CancellationToken.None);

        user.PhotoMetadata.Should().NotBeNull();
        photoRepo.Verify(r => r.Add(user.PhotoMetadata!), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_DoesNothing()
    {
        var userRepo = new Mock<IUserRepository>();
        var photoRepo = new Mock<IPhotoMetadataRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        userRepo.Setup(r => r.GetById(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var sut = new UserPhotoUploadedHandler(userRepo.Object, photoRepo.Object, unitOfWork.Object);
        var message = new PhotoUploadedEvent(
            Guid.NewGuid(), "storage-key", "avatar.png", "image/png", 1024, "User", Guid.NewGuid(), DateTime.UtcNow);

        await sut.Handle(message, CancellationToken.None);

        photoRepo.Verify(r => r.Add(It.IsAny<PhotoMetadata>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
