namespace CoffeePeek.Moderation.Domain.Aggregates;

public interface IModerationRoasterRepository
{
    Task<ModerationRoaster?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ModerationRoaster roaster);
}
