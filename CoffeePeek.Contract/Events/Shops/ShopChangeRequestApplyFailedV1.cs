namespace CoffeePeek.Contract.Events.Shops;

/// <summary>
/// Shops could not apply an approved change request (the shop or referenced catalog items changed since it was
/// submitted). Lets Moderation stop showing the request as Approved.
/// </summary>
public record ShopChangeRequestApplyFailedV1(
    Guid RequestId,
    Guid ShopId,
    string Reason,
    DateTime FailedAtUtc);
