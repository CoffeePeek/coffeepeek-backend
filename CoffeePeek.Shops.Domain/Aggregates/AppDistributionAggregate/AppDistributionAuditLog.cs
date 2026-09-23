using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

public sealed class AppDistributionAuditLog : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public string Action { get; private set; } = null!;
    public Guid? EntityId { get; private set; }
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private AppDistributionAuditLog()
    {
    }

    private AppDistributionAuditLog(Guid userId, string action, Guid? entityId, string? oldValue, string? newValue)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Action = action;
        EntityId = entityId;
        OldValue = TrimToLimit(oldValue, BusinessConstants.MaxAuditValueLength);
        NewValue = TrimToLimit(newValue, BusinessConstants.MaxAuditValueLength);
    }

    public static AppDistributionAuditLog Create(
        Guid userId,
        string action,
        Guid? entityId,
        string? oldValue,
        string? newValue) =>
        new AppDistributionAuditLog(
            userId,
            TrimToLimit(action, BusinessConstants.MaxAuditActionLength) ?? action,
            entityId,
            oldValue,
            newValue);

    private static string? TrimToLimit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
