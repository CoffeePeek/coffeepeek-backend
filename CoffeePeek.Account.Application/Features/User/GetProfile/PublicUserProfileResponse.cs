namespace CoffeePeek.Account.Application.Features.User.GetProfile;

public record PublicUserProfileResponse(
    string UserName,
    DateTime CreatedAtUtc,
    string? About,
    string? AvatarUrl,
    int CheckInCount,
    int AddedShopsCount);
