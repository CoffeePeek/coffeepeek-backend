namespace CoffeePeek.Account.Application.Features.User.GetProfile;

public record UserProfileResponse(
    string UserName,
    string Email,
    DateTime CreatedAtUtc,
    string? About,
    string? AvatarUrl,
    int ReviewCount,
    int CheckInCount,
    int AddedShopsCount
)
{
    public CoffeePeek.Contract.Dtos.PublicAddresses.PublicAddressDto? Address { get; init; }
}
