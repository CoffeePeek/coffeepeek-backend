namespace CoffeePeek.Contract.Dtos.Profiles;

public sealed record ProfileFollowPageDto(ProfileFollowUserDto[] Items, int TotalCount, int Page, int PageSize);
