using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public sealed record CheckInPhotoAccess(Guid CheckInId, Guid OwnerId, Guid PhotoId,
    string ContentType, CheckInKind Kind, int ContentRevision);
