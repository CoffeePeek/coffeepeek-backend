namespace CoffeePeek.Contract.Events.Account;

public record AccountDeletionRequestedV1(
    Guid DeletionId,
    Guid UserId,
    DateTime RequestedAtUtc);
