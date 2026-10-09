using CoffeePeek.Contract.Events;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public static class RoasterPhotoCommitProbeHandler
{
    public static Task Handle(ShopPhotosAttachedEvent message, RoasterPhotoCommitObserver observer) =>
        observer.ObserveAsync(message);
}
