namespace CoffeePeek.Account.Application.Features.User.GetProfile;

public record UserProfileResponse(
    string UserName,
    string Email,
    DateTime CreatedAtUtc,
    string? About,
    string? AvatarUrl,
    int CheckInCount,
    int AddedShopsCount
)
{
    public int AddedRoastersCount { get; init; }
    public int FollowersCount { get; init; }
    public CoffeePeek.Contract.Dtos.PublicAddresses.PublicAddressDto? Address { get; init; }
}
