namespace CoffeePeek.Account.Domain.Entities.Notifications;

public sealed class PushDevice
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public Guid Revision { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
