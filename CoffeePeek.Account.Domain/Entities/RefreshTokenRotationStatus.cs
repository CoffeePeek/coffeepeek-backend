namespace CoffeePeek.Account.Domain.Entities;

public enum RefreshTokenRotationStatus
{
    Success,
    Invalid,
    Expired,
    Reused
}
