using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Profiles;

public sealed record BlockedAuthorDto(Guid AuthorId, string UserName, string? AvatarUrl,
    PublicAddressDto? Address, DateTime CreatedAtUtc);
