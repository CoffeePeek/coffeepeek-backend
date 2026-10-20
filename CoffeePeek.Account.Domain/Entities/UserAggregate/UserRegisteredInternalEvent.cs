using CoffeePeek.Shared.Domain.Events;

namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public record UserRegisteredInternalEvent(
    Guid UserId,
    string Email,
    string Username,
    string ConfirmationToken) : IImmediateEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    /// <summary>The confirmation email for the user's current address and token.</summary>
    public static UserRegisteredInternalEvent For(User user) =>
        new(user.Id, user.Credentials.Email.Value, user.Username.Value, user.Credentials.EmailConfirmationToken!);
}
