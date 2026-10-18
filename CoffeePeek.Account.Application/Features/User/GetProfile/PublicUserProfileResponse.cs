namespace CoffeePeek.Account.Application.Features.User.GetProfile;

public record PublicUserProfileResponse(
    string UserName,
    DateTime CreatedAtUtc,
    string? About,
    string? AvatarUrl,
    int CheckInCount,
    int AddedShopsCount)
{
    public Guid AuthorId { get; init; }
    public bool IsBlockedByViewer { get; init; }
    public int AddedRoastersCount { get; init; }
    public int FollowersCount { get; init; }
    public bool IsFollowing { get; init; }
}
