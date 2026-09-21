using CoffeePeek.Shared.Domain.Events;

namespace CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

public record AccountDeletionRequestedInternalEvent(
    Guid RequestId,
    Guid UserId,
    string Email,
    string Username,
    string ConfirmationToken) : IImmediateEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
