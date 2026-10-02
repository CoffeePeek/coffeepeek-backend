namespace CoffeePeek.Moderation.Domain.Aggregates;

public interface IModerationRoasterRepository
{
    Task<ModerationRoaster?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ModerationRoaster roaster);
    Task<bool> IsAwaitingGeocodingAsync(Guid id, string submittedAddress, CancellationToken ct = default);
    Task<bool> TryApplyGeocodingAsync(
        Guid id, string submittedAddress, decimal latitude, decimal longitude, CancellationToken ct = default);
}
