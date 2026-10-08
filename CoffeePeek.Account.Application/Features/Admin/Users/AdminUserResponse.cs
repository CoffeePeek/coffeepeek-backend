namespace CoffeePeek.Account.Application.Features.Admin.Users;

public record AdminUserResponse(
    Guid Id,
    string UserName,
    string Email,
    DateTime CreatedAtUtc,
    string? About,
    string? AvatarUrl,
    int CheckInCount,
    int AddedShopsCount,
    string[] Roles,
    bool IsBlocked);
