using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Domain.Entities.PhotoMetadataAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.Account.Infrastructure.Consumers;

public class UserPhotoUploadedHandler(
    IUserRepository userRepository,
    IPhotoMetadataRepository photoMetadataRepository,
    IUnitOfWork unitOfWork)
{
    public async Task Handle(PhotoUploadedEvent message, CancellationToken ct)
    {
        var user = await userRepository.GetById(message.OwnerId, ct);

        if (user == null) return;

        var photo = PhotoMetadata.Create(
            message.FileName,
            message.ContentType,
            message.StorageKey,
            message.SizeBytes);

        photoMetadataRepository.Add(photo);
        user.UpdateAvatar(photo);

        await unitOfWork.SaveChangesAsync(ct);
    }
}