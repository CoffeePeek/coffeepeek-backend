namespace CoffeePeek.Shops.Application.Features.CheckIn;

/// <summary>Current authorization and metadata for one photo, without projecting the check-in.</summary>
public interface ICheckInPhotoAccessQueries
{
    Task<CheckInPhotoAccess?> GetAsync(Guid checkInId, Guid photoId, Guid? readerId, CancellationToken ct);
    Task<CheckInPhotoAccess?> GetModerationAsync(Guid checkInId, Guid photoId, int revision, CancellationToken ct);
}
