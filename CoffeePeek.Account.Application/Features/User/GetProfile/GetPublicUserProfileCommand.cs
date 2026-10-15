namespace CoffeePeek.Account.Application.Features.User.GetProfile;

public record GetPublicUserProfileCommand(Guid UserId)
{
    public Guid? ViewerId { get; init; }
}
